using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>Resultado de abrir um arquivo de Trabalho de torno.</summary>
public sealed record LatheLoadResult(LatheJob? Job, string? Error)
{
    public bool Ok => Job is not null;

    /// <summary>Avisos de um trabalho que abriu (ex.: fixação inválida, que fica "não informada"), como na fresa.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Salvar e abrir o <see cref="LatheJob"/> em JSON com versão (mesmo cuidado do <see cref="CamJobJson"/>: sem reflexão, números com ponto).</summary>
public static class LatheJobJson
{
    internal const string Format = "casillas-job-torno";

    /// <summary>
    /// Texto do arquivo. <paramref name="keepEmptyFields"/> (TORNO-T1, o Salvar da tela): grava em cada operação a lista dos campos
    /// que o usuário deixou vazios, para o trabalho abrir Incompleto como estava. Sem ele, a lista não é gravada (como antes).
    /// </summary>
    public static string ToJson(LatheJob job, bool keepEmptyFields = false)
    {
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = LatheJob.CurrentVersion,
            ["name"] = job.Name,
            ["dialect"] = job.Dialect.ToString(),
        };
        // TORNO-T1: Programa nº, G5x e refrigeração vazios não vão para o arquivo (nada suposto); com valor, na ordem de sempre
        if (job.ProgramNumber is { } number)
        {
            root["programNumber"] = number;
        }

        if (job.WorkOffset is { } offset)
        {
            root["workOffset"] = offset;
        }

        if (job.Coolant is { } coolant)
        {
            root["coolant"] = coolant;
        }

        if (job.Stock is { IsEmpty: false } stock)
        {
            var so = new JsonObject();
            if (stock.Diameter is { } sd)
            {
                so["diameter"] = sd;
            }

            if (stock.Length is { } sl)
            {
                so["length"] = sl;
            }

            if (stock.FaceZ is { } sz)
            {
                so["faceZ"] = sz;
            }

            root["stock"] = so;
        }

        if (job.Profile is { } p)
        {
            root["profile"] = CamJobJson.SegmentsToJson(p.Segments);
        }

        // campo opcional: arquivo sem ele (antigo ou sem fixação) abre com a fixação "não informada"
        if (job.Fixation is { } fx)
        {
            var o = new JsonObject { ["kind"] = LatheFixation.FileName(fx.Kind) };
            if (fx.GripDiameter is { } d)
            {
                o["gripDiameter"] = d;
            }

            if (fx.GripLength is { } l)
            {
                o["gripLength"] = l;
            }

            if (fx.StickOut is { } s)
            {
                o["stickOut"] = s;
            }

            root["fixation"] = o;
        }

        var ops = new JsonArray();
        foreach (LatheOperationSpec s in job.Operations)
        {
            ops.Add(OpToJson(s, keepEmptyFields));
        }

        root["operations"] = ops;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject OpToJson(LatheOperationSpec s, bool keepEmptyFields)
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
        // TORNO-T1: campos que o usuário deixou vazios (nome da tela); o número guardado deles é 0 e nunca é usado
        if (keepEmptyFields && s.MissingFields.Count > 0)
        {
            o["missing"] = new JsonArray([.. s.MissingFields.Select(f => (JsonNode)f)]);
        }

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

        if (s.FaceGroove is { } fg)
        {
            o["faceGroove"] = new JsonObject
            {
                ["outerDiameter"] = fg.OuterDiameter,
                ["innerDiameter"] = fg.InnerDiameter,
                ["faceZ"] = fg.FaceZ,
                ["bottomZ"] = fg.BottomZ,
                ["toolWidth"] = fg.ToolWidth,
                ["peck"] = fg.Peck,
                ["retract"] = fg.Retract,
                ["feed"] = fg.Feed,
                ["reducedFeed"] = fg.ReducedFeed,
                ["reducedFromDiameter"] = fg.ReducedFromDiameter,
                ["allowance"] = fg.Allowance,
                ["toolMinDiameter"] = fg.ToolMinDiameter,
                ["toolMaxDiameter"] = fg.ToolMaxDiameter,
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
                // chave ausente = vazio (TORNO-T1); arquivo antigo sempre traz as três
                ProgramNumber = root["programNumber"] is null ? null : (int)CamJobJson.Num(root, "programNumber"),
                WorkOffset = root["workOffset"] is null ? null : (int)CamJobJson.Num(root, "workOffset"),
                Coolant = root["coolant"] is null ? null : CamJobJson.Bool(root, "coolant"),
            };
            if (root["stock"] is JsonObject so)
            {
                double? Opt(string key) => so[key] is null ? null : CamJobJson.Num(so, key);
                var stock = new LatheJobStock(Opt("diameter"), Opt("length"), Opt("faceZ"));
                if (stock.Validate() is { } stockError)
                {
                    throw new CamJobJson.FormatException(stockError);
                }

                job.Stock = stock.IsEmpty ? null : stock;
            }

            if (root["profile"] is not null)
            {
                job.Profile = LatheProfile.From(CamJobJson.SegmentsFrom(root["profile"], "Perfil inválido no arquivo."), out string? error)
                    ?? throw new CamJobJson.FormatException(error ?? "Perfil inválido no arquivo.");
            }

            // fixação estragada não impede de abrir (igual à fresa): fica "não informada" e vira aviso
            var warnings = new List<string>();
            if (root["fixation"] is not null)
            {
                try
                {
                    job.Fixation = FixationFrom(root["fixation"] as JsonObject ?? throw new CamJobJson.FormatException("formato inválido"));
                }
                catch (CamJobJson.FormatException ex)
                {
                    warnings.Add($"Fixação do arquivo ignorada ({ex.Message.TrimEnd('.')}): ficou como não informada.");
                }
            }

            foreach (JsonNode? on in CamJobJson.Arr(root, "operations"))
            {
                job.Operations.Add(OpFrom(on as JsonObject ?? throw new CamJobJson.FormatException("Operação inválida.")));
            }

            IReadOnlyList<string> problems = job.Validate();
            return problems.Count > 0 ? new LatheLoadResult(null, problems[0]) : new LatheLoadResult(job, null) { Warnings = warnings };
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

    private static LatheFixation FixationFrom(JsonObject o)
    {
        string name = CamJobJson.Str(o, "kind");
        LatheFixationKind kind = LatheFixation.FromFileName(name) ?? throw new CamJobJson.FormatException($"Valor desconhecido de fixação: \"{name}\".");
        double? Opt(string key) => o[key] is null ? null : CamJobJson.Num(o, key);
        var f = new LatheFixation(kind, Opt("gripDiameter"), Opt("gripLength"), Opt("stickOut"));
        return f.Validate() is { } error ? throw new CamJobJson.FormatException(error) : f;
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
        if (o["missing"] is JsonArray missing)
        {
            spec = spec with { MissingFields = [.. missing.Select(m => m is JsonValue v && v.TryGetValue(out string? name) ? name : throw new CamJobJson.FormatException("Campo vazio inválido na operação."))] };
        }

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

        if (o["faceGroove"] is JsonObject fg)
        {
            spec = spec with
            {
                FaceGroove = new FaceGroovingParams(
                    CamJobJson.Num(fg, "outerDiameter"), CamJobJson.Num(fg, "innerDiameter"), CamJobJson.Num(fg, "faceZ"), CamJobJson.Num(fg, "bottomZ"),
                    CamJobJson.Num(fg, "toolWidth"), CamJobJson.Num(fg, "peck"), CamJobJson.Num(fg, "retract"), CamJobJson.Num(fg, "feed"),
                    CamJobJson.Num(fg, "reducedFeed"), CamJobJson.Num(fg, "reducedFromDiameter"), CamJobJson.Num(fg, "allowance"),
                    CamJobJson.Num(fg, "toolMinDiameter"), CamJobJson.Num(fg, "toolMaxDiameter"), spec.Clearance),
            };
        }

        return spec;
    }
}
