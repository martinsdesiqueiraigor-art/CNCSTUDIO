using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>Resultado de abrir um arquivo de Job: o Job ou o motivo de não ter aberto.</summary>
public sealed record JobLoadResult(CamJob? Job, string? Error)
{
    public bool Ok => Job is not null;
}

/// <summary>
/// Salvar e abrir o <see cref="CamJob"/> em JSON com versão. Montado à mão com <see cref="JsonObject"/> (sem reflexão),
/// para funcionar também no WebAssembly. Números sempre com ponto, independente do idioma do computador.
/// </summary>
public static class CamJobJson
{
    private const string Format = "casillas-job";

    internal sealed class FormatException(string message) : Exception(message);

    public static string ToJson(CamJob job)
    {
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = CamJob.CurrentVersion,
            ["name"] = job.Name,
            ["dialect"] = job.Dialect.ToString(),
            ["programNumber"] = job.ProgramNumber,
            ["workOffset"] = job.WorkOffset,
            ["coolant"] = job.Coolant,
        };
        if (job.Stock is { } st)
        {
            root["stock"] = new JsonObject { ["x"] = st.X, ["y"] = st.Y, ["width"] = st.Width, ["length"] = st.Length, ["thickness"] = st.Thickness };
        }

        var contours = new JsonArray();
        foreach (Contour c in job.Contours)
        {
            JsonArray segs = SegmentsToJson(c.Segments);
            contours.Add(segs);
        }

        root["contours"] = contours;
        var ops = new JsonArray();
        foreach (CamOperationSpec s in job.Operations)
        {
            ops.Add(OpToJson(s));
        }

        root["operations"] = ops;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    internal static JsonArray SegmentsToJson(IReadOnlyList<Segment2> segments)
    {
        var segs = new JsonArray();
        foreach (Segment2 s in segments)
        {
            var a = new JsonArray(s.Start.X, s.Start.Y, s.End.X, s.End.Y);
            if (s.IsArc)
            {
                a.Add(JsonValue.Create(s.Center.X));
                a.Add(JsonValue.Create(s.Center.Y));
                a.Add(JsonValue.Create(s.Ccw ? 1 : 0));
            }

            segs.Add(a);
        }

        return segs;
    }

    private static JsonObject OpToJson(CamOperationSpec s)
    {
        var o = new JsonObject
        {
            ["kind"] = s.Kind.ToString(),
            ["enabled"] = s.Enabled,
            ["label"] = s.Label,
            ["contour"] = s.ContourIndex,
            ["tool"] = new JsonObject
            {
                ["number"] = s.Tool.Number,
                ["diameter"] = s.Tool.Diameter,
                ["rpm"] = s.Tool.SpindleRpm,
                ["feedXY"] = s.Tool.FeedXY,
                ["feedZ"] = s.Tool.FeedZ,
                ["name"] = s.Tool.Name,
            },
            ["levels"] = new JsonObject
            {
                ["safe"] = s.Levels.SafeZ,
                ["clearance"] = s.Levels.ClearanceZ,
                ["top"] = s.Levels.TopZ,
                ["bottom"] = s.Levels.BottomZ,
                ["stepDown"] = s.Levels.StepDown,
            },
        };
        if (s.ContourParams is { } c)
        {
            o["contourParams"] = new JsonObject { ["side"] = c.Side.ToString(), ["climb"] = c.Climb, ["comp"] = c.ControllerComp, ["allowance"] = c.Allowance, ["lead"] = c.LeadRadius };
        }

        if (s.PocketParams is { } p)
        {
            o["pocketParams"] = new JsonObject { ["stepOver"] = p.StepOverPercent, ["finish"] = p.FinishAllowance, ["lead"] = p.LeadRadius };
            if (s.Islands)
            {
                o["islands"] = true;
            }
        }

        if (s.Kind == JobOpKind.Facing)
        {
            o["facingStepOver"] = s.FacingStepOverPercent;
        }

        if (s.Drill is { } d)
        {
            o["drill"] = new JsonObject { ["cycle"] = d.Cycle.ToString(), ["peck"] = d.Peck, ["dwell"] = d.DwellSeconds };
        }

        if (s.Wrap is { } w)
        {
            o["wrap"] = new JsonObject
            {
                ["text"] = w.Text,
                ["letterHeight"] = w.LetterHeight,
                ["axis"] = w.Axis,
                ["diameter"] = w.Setup.Diameter,
                ["startAxial"] = w.Setup.StartAxial,
                ["startAngle"] = w.Setup.StartAngle,
                ["depth"] = w.Setup.Depth,
                ["passDepth"] = w.Setup.PassDepth,
                ["clearance"] = w.Setup.Clearance,
                ["surfaceFeed"] = w.Setup.SurfaceFeed,
                ["plungeFeed"] = w.Setup.PlungeFeed,
            };
        }

        if (s.Face is { } f)
        {
            o["face"] = new JsonObject { ["axis"] = f.Axis, ["angle"] = f.Angle, ["axisToFace"] = f.AxisToFace };
        }

        return o;
    }

    public static JobLoadResult FromJson(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root)
            {
                return new JobLoadResult(null, "O arquivo não é um Job do Casillas CAM.");
            }

            if (Str(root, "format") == LatheJobJson.Format)
            {
                return new JobLoadResult(null, "Este trabalho é de torno. Abra-o no botão Torno da aba Trabalho.");
            }

            if (Str(root, "format") != Format)
            {
                return new JobLoadResult(null, "O arquivo não é um Job do Casillas CAM.");
            }

            int version = (int)Num(root, "version");
            if (version > CamJob.CurrentVersion)
            {
                return new JobLoadResult(null, $"Este Job é de uma versão mais nova ({version}). Atualize o Casillas Code para abrir.");
            }

            if (version < 1)
            {
                return new JobLoadResult(null, "Versão do Job inválida.");
            }

            var job = new CamJob
            {
                Name = Str(root, "name"),
                Dialect = Enum<CamDialect>(Str(root, "dialect"), "comando"),
                ProgramNumber = (int)Num(root, "programNumber"),
                WorkOffset = (int)Num(root, "workOffset"),
                Coolant = Bool(root, "coolant"),
            };
            if (root["stock"] is JsonObject st)
            {
                job.Stock = new JobStock(Num(st, "x"), Num(st, "y"), Num(st, "width"), Num(st, "length"), Num(st, "thickness"));
            }

            foreach (JsonNode? cn in Arr(root, "contours"))
            {
                job.Contours.Add(ContourFrom(cn));
            }

            foreach (JsonNode? on in Arr(root, "operations"))
            {
                job.Operations.Add(OpFrom(on as JsonObject ?? throw new FormatException("Operação inválida.")));
            }

            IReadOnlyList<string> problems = job.Validate();
            return problems.Count > 0 ? new JobLoadResult(null, problems[0]) : new JobLoadResult(job, null);
        }
        catch (JsonException)
        {
            return new JobLoadResult(null, "O arquivo está corrompido (não é um JSON válido).");
        }
        catch (FormatException ex)
        {
            return new JobLoadResult(null, ex.Message);
        }
    }

    private static Contour ContourFrom(JsonNode? node) => new(SegmentsFrom(node, "Contorno inválido no arquivo."));

    internal static List<Segment2> SegmentsFrom(JsonNode? node, string emptyMessage)
    {
        if (node is not JsonArray segs || segs.Count == 0)
        {
            throw new FormatException(emptyMessage);
        }

        var list = new List<Segment2>();
        foreach (JsonNode? sn in segs)
        {
            if (sn is not JsonArray a || (a.Count != 4 && a.Count != 7))
            {
                throw new FormatException("Trecho inválido no arquivo.");
            }

            double[] v = a.Select(n => n is JsonValue jv && jv.TryGetValue(out double d) && double.IsFinite(d) ? d : throw new FormatException("Número inválido no desenho.")).ToArray();
            list.Add(a.Count == 4
                ? Segment2.Line(new Point2(v[0], v[1]), new Point2(v[2], v[3]))
                : Segment2.Arc(new Point2(v[0], v[1]), new Point2(v[2], v[3]), new Point2(v[4], v[5]), v[6] != 0));
        }

        return list;
    }

    private static CamOperationSpec OpFrom(JsonObject o)
    {
        JsonObject t = Obj(o, "tool"), l = Obj(o, "levels");
        var spec = new CamOperationSpec(
            Enum<JobOpKind>(Str(o, "kind"), "operação"),
            new CamTool((int)Num(t, "number"), Num(t, "diameter"), Num(t, "rpm"), Num(t, "feedXY"), Num(t, "feedZ"), Str(t, "name")),
            new CamLevels(Num(l, "safe"), Num(l, "clearance"), Num(l, "top"), Num(l, "bottom"), Num(l, "stepDown")))
        {
            Enabled = Bool(o, "enabled"),
            Label = Str(o, "label"),
            ContourIndex = (int)Num(o, "contour"),
        };
        if (o["contourParams"] is JsonObject c)
        {
            spec = spec with { ContourParams = new ContourParams(Enum<ContourSide>(Str(c, "side"), "lado do contorno"), Bool(c, "climb"), Bool(c, "comp"), Num(c, "allowance"), Num(c, "lead")) };
        }

        if (o["pocketParams"] is JsonObject p)
        {
            spec = spec with { PocketParams = new PocketParams(Num(p, "stepOver"), Num(p, "finish"), Num(p, "lead")), Islands = o["islands"] is not null && Bool(o, "islands") };
        }

        if (o["facingStepOver"] is not null)
        {
            spec = spec with { FacingStepOverPercent = Num(o, "facingStepOver") };
        }

        if (o["drill"] is JsonObject d)
        {
            spec = spec with { Drill = new DrillSpec(Enum<DrillCycleKind>(Str(d, "cycle"), "ciclo de furação"), Num(d, "peck"), Num(d, "dwell")) };
        }

        if (o["wrap"] is JsonObject w)
        {
            var setup = new WrapSetup(Num(w, "diameter"), Num(w, "startAxial"), Num(w, "startAngle"), Num(w, "depth"), Num(w, "passDepth"), Num(w, "clearance"), Num(w, "surfaceFeed"), Num(w, "plungeFeed"));
            spec = spec with { Wrap = new WrapSpec(Str(w, "text"), Num(w, "letterHeight"), Str(w, "axis"), setup) };
        }

        if (o["face"] is JsonObject f)
        {
            spec = spec with { Face = new RotaryFaceSpec(Str(f, "axis"), Num(f, "angle"), Num(f, "axisToFace")) };
        }

        return spec;
    }

    internal static JsonObject Obj(JsonObject o, string key) => o[key] as JsonObject ?? throw new FormatException($"Falta o bloco \"{key}\" no arquivo.");

    internal static JsonArray Arr(JsonObject o, string key) => o[key] as JsonArray ?? throw new FormatException($"Falta a lista \"{key}\" no arquivo.");

    internal static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out string? s) && s is not null ? s : throw new FormatException($"Falta o texto \"{key}\" no arquivo.");

    internal static double Num(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : throw new FormatException($"Falta o número \"{key}\" no arquivo.");

    internal static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : throw new FormatException($"Falta o valor sim/não \"{key}\" no arquivo.");

    internal static T Enum<T>(string name, string what)
        where T : struct, Enum =>
        System.Enum.TryParse(name, false, out T value) && System.Enum.IsDefined(value) ? value : throw new FormatException($"Valor desconhecido de {what}: \"{name}\".");
}
