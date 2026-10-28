using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>Resultado de abrir um arquivo de Trabalho de torno.</summary>
public sealed record LatheLoadResult(LatheJob? Job, string? Error)
{
    public bool Ok => Job is not null;
}

/// <summary>Salvar e abrir o <see cref="LatheJob"/> em JSON com versão (mesmo cuidado do <see cref="CamJobJson"/>: sem reflexão, números com ponto).</summary>
public static class LatheJobJson
{
    internal const string Format = "casillas-job-torno";

    public static string ToJson(LatheJob job)
    {
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = LatheJob.CurrentVersion,
            ["name"] = job.Name,
            ["dialect"] = job.Dialect.ToString(),
            ["programNumber"] = job.ProgramNumber,
            ["workOffset"] = job.WorkOffset,
            ["coolant"] = job.Coolant,
        };
        if (job.Profile is { } p)
        {
            root["profile"] = CamJobJson.SegmentsToJson(p.Segments);
        }

        var ops = new JsonArray();
        foreach (LatheOperationSpec s in job.Operations)
        {
            ops.Add(OpToJson(s));
        }

        root["operations"] = ops;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject OpToJson(LatheOperationSpec s)
    {
        var o = new JsonObject
        {
            ["kind"] = s.Kind.ToString(),
            ["enabled"] = s.Enabled,
            ["label"] = s.Label,
            ["safeZ"] = s.SafeZ,
            ["safeDiameter"] = s.SafeDiameter,
            ["clearance"] = s.Clearance,
            ["tool"] = new JsonObject
            {
                ["number"] = s.Tool.Number,
                ["name"] = s.Tool.Name,
                ["surfaceSpeed"] = s.Tool.SurfaceSpeed,
                ["maxRpm"] = s.Tool.MaxRpm,
                ["rpm"] = s.Tool.Rpm,
            },
        };
        if (s.Turning is { } t)
        {
            o["turning"] = new JsonObject
            {
                ["stockDiameter"] = t.StockDiameter,
                ["internal"] = t.Internal,
                ["rough"] = t.Rough,
                ["finish"] = t.Finish,
                ["depthOfCut"] = t.DepthOfCut,
                ["retract"] = t.Retract,
                ["allowanceR"] = t.AllowanceR,
                ["allowanceZ"] = t.AllowanceZ,
                ["roughFeed"] = t.RoughFeed,
                ["finishFeed"] = t.FinishFeed,
            };
        }

        if (s.Facing is { } f)
        {
            o["facing"] = new JsonObject
            {
                ["stockDiameter"] = f.StockDiameter,
                ["innerDiameter"] = f.InnerDiameter,
                ["stockFaceZ"] = f.StockFaceZ,
                ["finalFaceZ"] = f.FinalFaceZ,
                ["depthOfCut"] = f.DepthOfCut,
                ["feed"] = f.Feed,
            };
        }

        if (s.Drill is { } d)
        {
            o["drill"] = new JsonObject { ["topZ"] = d.TopZ, ["bottomZ"] = d.BottomZ, ["peck"] = d.Peck, ["feed"] = d.Feed };
        }

        if (s.Thread is { } th)
        {
            o["thread"] = new JsonObject
            {
                ["internal"] = th.Internal,
                ["majorDiameter"] = th.MajorDiameter,
                ["pitch"] = th.Pitch,
                ["startZ"] = th.StartZ,
                ["endZ"] = th.EndZ,
                ["rpm"] = th.Rpm,
                ["height"] = th.Height,
                ["firstDepth"] = th.FirstDepth,
                ["minDepth"] = th.MinDepth,
                ["finishPasses"] = th.FinishPasses,
                ["allowance"] = th.Allowance,
                ["approach"] = th.Approach,
                ["overrun"] = th.Overrun,
            };
        }

        if (s.Groove is { } g)
        {
            o["groove"] = new JsonObject
            {
                ["internal"] = g.Internal,
                ["surfaceDiameter"] = g.SurfaceDiameter,
                ["bottomDiameter"] = g.BottomDiameter,
                ["zStart"] = g.ZStart,
                ["width"] = g.Width,
                ["toolWidth"] = g.ToolWidth,
                ["peck"] = g.Peck,
                ["feed"] = g.Feed,
                ["retract"] = g.Retract,
                ["allowance"] = g.Allowance,
                ["finish"] = g.Finish,
            };
        }

        return o;
    }

    public static LatheLoadResult FromJson(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root)
            {
                return new LatheLoadResult(null, "O arquivo não é um Trabalho de torno do Casillas CAM.");
            }

            string format = CamJobJson.Str(root, "format");
            if (format == "casillas-job")
            {
                return new LatheLoadResult(null, "Este trabalho é de fresa. Abra-o no botão Fresa da aba Trabalho.");
            }

            if (format != Format)
            {
                return new LatheLoadResult(null, "O arquivo não é um Trabalho de torno do Casillas CAM.");
            }

            int version = (int)CamJobJson.Num(root, "version");
            if (version > LatheJob.CurrentVersion)
            {
                return new LatheLoadResult(null, $"Este trabalho é de uma versão mais nova ({version}). Atualize o Casillas Code para abrir.");
            }

            if (version < 1)
            {
                return new LatheLoadResult(null, "Versão do trabalho inválida.");
            }

            var job = new LatheJob
            {
                Name = CamJobJson.Str(root, "name"),
                Dialect = CamJobJson.Enum<CamDialect>(CamJobJson.Str(root, "dialect"), "comando"),
                ProgramNumber = (int)CamJobJson.Num(root, "programNumber"),
                WorkOffset = (int)CamJobJson.Num(root, "workOffset"),
                Coolant = CamJobJson.Bool(root, "coolant"),
            };
            if (root["profile"] is not null)
            {
                job.Profile = LatheProfile.From(CamJobJson.SegmentsFrom(root["profile"], "Perfil inválido no arquivo."), out string? error)
                    ?? throw new CamJobJson.FormatException(error ?? "Perfil inválido no arquivo.");
            }

            foreach (JsonNode? on in CamJobJson.Arr(root, "operations"))
            {
                job.Operations.Add(OpFrom(on as JsonObject ?? throw new CamJobJson.FormatException("Operação inválida.")));
            }

            IReadOnlyList<string> problems = job.Validate();
            return problems.Count > 0 ? new LatheLoadResult(null, problems[0]) : new LatheLoadResult(job, null);
        }
        catch (JsonException)
        {
            return new LatheLoadResult(null, "O arquivo está corrompido (não é um JSON válido).");
        }
        catch (CamJobJson.FormatException ex)
        {
            return new LatheLoadResult(null, ex.Message);
        }
    }

    private static LatheOperationSpec OpFrom(JsonObject o)
    {
        JsonObject t = CamJobJson.Obj(o, "tool");
        var spec = new LatheOperationSpec(
            CamJobJson.Enum<LatheOpKind>(CamJobJson.Str(o, "kind"), "operação"),
            new LatheTool((int)CamJobJson.Num(t, "number"), CamJobJson.Str(t, "name"), CamJobJson.Num(t, "surfaceSpeed"), CamJobJson.Num(t, "maxRpm"), CamJobJson.Num(t, "rpm")))
        {
            Enabled = CamJobJson.Bool(o, "enabled"),
            Label = CamJobJson.Str(o, "label"),
            SafeZ = CamJobJson.Num(o, "safeZ"),
            SafeDiameter = CamJobJson.Num(o, "safeDiameter"),
            Clearance = CamJobJson.Num(o, "clearance"),
        };
        if (o["turning"] is JsonObject tu)
        {
            spec = spec with
            {
                Turning = new TurningParams(
                    CamJobJson.Num(tu, "stockDiameter"), CamJobJson.Bool(tu, "internal"), CamJobJson.Bool(tu, "rough"), CamJobJson.Bool(tu, "finish"), spec.Clearance,
                    CamJobJson.Num(tu, "depthOfCut"), CamJobJson.Num(tu, "retract"), CamJobJson.Num(tu, "allowanceR"), CamJobJson.Num(tu, "allowanceZ"),
                    CamJobJson.Num(tu, "roughFeed"), CamJobJson.Num(tu, "finishFeed")),
            };
        }

        if (o["facing"] is JsonObject f)
        {
            spec = spec with
            {
                Facing = new FacingSpec(CamJobJson.Num(f, "stockDiameter"), CamJobJson.Num(f, "innerDiameter"), CamJobJson.Num(f, "stockFaceZ"), CamJobJson.Num(f, "finalFaceZ"), CamJobJson.Num(f, "depthOfCut"), CamJobJson.Num(f, "feed")),
            };
        }

        if (o["drill"] is JsonObject d)
        {
            spec = spec with { Drill = new LatheDrillSpec(CamJobJson.Num(d, "topZ"), CamJobJson.Num(d, "bottomZ"), CamJobJson.Num(d, "peck"), CamJobJson.Num(d, "feed")) };
        }

        if (o["thread"] is JsonObject th)
        {
            spec = spec with
            {
                Thread = new ThreadingParams(
                    CamJobJson.Bool(th, "internal"), CamJobJson.Num(th, "majorDiameter"), CamJobJson.Num(th, "pitch"), CamJobJson.Num(th, "startZ"), CamJobJson.Num(th, "endZ"), CamJobJson.Num(th, "rpm"),
                    CamJobJson.Num(th, "height"), CamJobJson.Num(th, "firstDepth"), CamJobJson.Num(th, "minDepth"), (int)CamJobJson.Num(th, "finishPasses"), CamJobJson.Num(th, "allowance"),
                    CamJobJson.Num(th, "approach"), CamJobJson.Num(th, "overrun"), spec.Clearance),
            };
        }

        if (o["groove"] is JsonObject g)
        {
            spec = spec with
            {
                Groove = new GroovingParams(
                    CamJobJson.Bool(g, "internal"), CamJobJson.Num(g, "surfaceDiameter"), CamJobJson.Num(g, "bottomDiameter"), CamJobJson.Num(g, "zStart"), CamJobJson.Num(g, "width"), CamJobJson.Num(g, "toolWidth"),
                    CamJobJson.Num(g, "peck"), CamJobJson.Num(g, "feed"), CamJobJson.Num(g, "retract"), CamJobJson.Num(g, "allowance"), CamJobJson.Bool(g, "finish"), spec.Clearance),
            };
        }

        return spec;
    }
}
