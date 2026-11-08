using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>Resultado de abrir um arquivo de Job: o Job ou o motivo de não ter aberto.</summary>
public sealed record JobLoadResult(CamJob? Job, string? Error)
{
    public bool Ok => Job is not null;

    /// <summary>Avisos de um Job que abriu (ex.: fixação inválida, que fica "não definida").</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
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
            // versão 1 enquanto nada do UX-04f é usado: o arquivo sai com o mesmo texto de antes
            ["version"] = CamJobStock.NeedsVersion2(job) ? 2 : 1,
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

        // campo opcional: versões antigas do app ignoram; sem ele, a fixação abre como "não definida"
        if (job.Fixture is { } fx)
        {
            root["fixture"] = new JsonObject { ["kind"] = fx.Kind, ["description"] = fx.Description };
        }

        // UX-04f (versão 2): altura da peça e formato novo do bruto; campo vazio não vai para o arquivo (nada suposto)
        if (job.PartHeight is { } partHeight)
        {
            root["partHeight"] = partHeight;
        }

        if (job.StockShape is { } shape)
        {
            root["stockShape"] = StockShapeToJson(shape);
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

    private static readonly (string Key, string Field)[] ShapeKeys =
    [
        ("xMinus", JobStockShape.XMinusField), ("xPlus", JobStockShape.XPlusField), ("yMinus", JobStockShape.YMinusField),
        ("yPlus", JobStockShape.YPlusField), ("zTop", JobStockShape.ZTopField), ("zBottom", JobStockShape.ZBottomField),
        ("diameter", JobStockShape.DiameterField), ("innerDiameter", JobStockShape.InnerField), ("length", JobStockShape.LengthField),
        ("offsetX", JobStockShape.OffsetXField), ("offsetY", JobStockShape.OffsetYField), ("offsetZ", JobStockShape.OffsetZField),
    ];

    private static double? ShapeValue(JobStockShape s, string key) => key switch
    {
        "xMinus" => s.XMinus, "xPlus" => s.XPlus, "yMinus" => s.YMinus, "yPlus" => s.YPlus, "zTop" => s.ZTop, "zBottom" => s.ZBottom,
        "diameter" => s.Diameter, "innerDiameter" => s.InnerDiameter, "length" => s.Length,
        "offsetX" => s.OffsetX, "offsetY" => s.OffsetY, _ => s.OffsetZ,
    };

    /// <summary>Formato novo do bruto: só os campos que o formato usa e que têm valor (vazio não vai para o arquivo).</summary>
    internal static JsonObject StockShapeToJson(JobStockShape s)
    {
        var o = new JsonObject { ["kind"] = s.Kind.ToString() };
        if (s.Kind == StockShapeKind.Step)
        {
            // STEP: caminho e resumo (nunca a malha); o deslocamento só no modo digitado
            if (!string.IsNullOrWhiteSpace(s.StepPath))
            {
                o["path"] = s.StepPath;
            }

            o["placement"] = s.Placement.ToString();
            if (s.StepSummary is { } b)
            {
                o["box"] = new JsonObject { ["minX"] = b.MinX, ["minY"] = b.MinY, ["minZ"] = b.MinZ, ["maxX"] = b.MaxX, ["maxY"] = b.MaxY, ["maxZ"] = b.MaxZ };
                o["volume"] = b.Volume;
            }
        }
        else if (s.Kind != StockShapeKind.Margins)
        {
            o["axis"] = s.Axis.ToString();
        }

        var used = s.Fields().Select(f => f.Name).ToHashSet();
        foreach ((string key, string field) in ShapeKeys)
        {
            // "diameter" guarda o Ø do cilindro e o Ø externo do tubo
            bool uses = used.Contains(field) || (key == "diameter" && used.Contains(JobStockShape.OuterField));
            if (uses && ShapeValue(s, key) is { } v)
            {
                o[key] = v;
            }
        }

        return o;
    }

    /// <summary>Lê o formato novo; null se o tipo ou o eixo não existem ou se um número é inválido. Campo ausente = vazio.</summary>
    internal static JobStockShape? StockShapeFrom(JsonNode? node)
    {
        if (node is not JsonObject o || o["kind"] is not JsonValue kv || !kv.TryGetValue(out string? kindText)
            || !System.Enum.TryParse(kindText, false, out StockShapeKind kind) || !System.Enum.IsDefined(kind))
        {
            return null;
        }

        var shape = new JobStockShape(kind) { OffsetX = null, OffsetY = null, OffsetZ = null };
        if (kind == StockShapeKind.Step)
        {
            if (StepFrom(o) is not { } step)
            {
                return null;
            }

            shape = step;
        }
        else if (kind != StockShapeKind.Margins)
        {
            if (o["axis"] is not JsonValue av || !av.TryGetValue(out string? axisText) || !System.Enum.TryParse(axisText, false, out StockAxis axis) || !System.Enum.IsDefined(axis))
            {
                return null;
            }

            shape = shape with { Axis = axis };
        }

        foreach ((string key, string field) in ShapeKeys)
        {
            if (o[key] is null)
            {
                continue;
            }

            if (o[key] is not JsonValue nv || !nv.TryGetValue(out double v) || !double.IsFinite(v))
            {
                return null;
            }

            shape = shape.With(field, v);
        }

        return shape;
    }

    /// <summary>Parte STEP do bruto: caminho, posição e resumo. Ausente = vazio (incompleto); presente e estragado = null.</summary>
    private static JobStockShape? StepFrom(JsonObject o)
    {
        var shape = new JobStockShape(StockShapeKind.Step) { OffsetX = null, OffsetY = null, OffsetZ = null };
        if (o["path"] is not null)
        {
            if (o["path"] is not JsonValue pv || !pv.TryGetValue(out string? path))
            {
                return null;
            }

            shape = shape with { StepPath = path };
        }

        if (o["placement"] is not null)
        {
            if (o["placement"] is not JsonValue lv || !lv.TryGetValue(out string? text) || !System.Enum.TryParse(text, false, out StepPlacement placement)
                || !System.Enum.IsDefined(placement))
            {
                return null;
            }

            shape = shape with { Placement = placement };
        }

        if (o["box"] is null && o["volume"] is null)
        {
            return shape;
        }

        if (o["box"] is not JsonObject b)
        {
            return null;
        }

        double? N(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : null;
        if (N(b["minX"]) is not { } x0 || N(b["minY"]) is not { } y0 || N(b["minZ"]) is not { } z0 || N(b["maxX"]) is not { } x1
            || N(b["maxY"]) is not { } y1 || N(b["maxZ"]) is not { } z1 || N(o["volume"]) is not { } volume)
        {
            return null;
        }

        var summary = new StepStockSummary(x0, y0, z0, x1, y1, z1, volume);
        return summary.Valid ? shape with { StepSummary = summary } : null;
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
            var face = new JsonObject { ["axis"] = f.Axis, ["angle"] = f.Angle, ["axisToFace"] = f.AxisToFace };
            if (f.Axis2 is { } axis2)
            {
                // 3+2: campos opcionais (arquivos sem eles continuam abrindo como face de um eixo)
                face["axis2"] = axis2;
                face["angle2"] = f.Angle2;
            }

            o["face"] = face;
        }

        // campo opcional (missão 11): versões antigas do app ignoram; sem ele, a operação abre sem marca
        if (s.Origin is { } origin)
        {
            var org = new JsonObject { ["source"] = origin.Source, ["typed"] = new JsonArray([.. origin.Typed.Select(t => (JsonNode)t)]) };
            if (origin.CutData is { } cut)
            {
                org["cutData"] = cut;
            }

            o["origin"] = org;
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

            var warnings = new List<string>();
            foreach (JsonNode? on in Arr(root, "operations"))
            {
                JsonObject oo = on as JsonObject ?? throw new FormatException("Operação inválida.");
                CamOperationSpec spec = OpFrom(oo);
                if (oo["origin"] is not null)
                {
                    // marca estragada não impede de abrir: a operação fica sem marca, com aviso
                    if (OriginFrom(oo["origin"]) is { } origin && origin.Validate() is null)
                    {
                        spec = spec with { Origin = origin };
                    }
                    else
                    {
                        warnings.Add($"Operação {job.Operations.Count + 1}: marca de origem do arquivo ignorada (formato inválido); ficou sem marca.");
                    }
                }

                job.Operations.Add(spec);
            }

            if (root["partHeight"] is JsonValue ph && ph.TryGetValue(out double partHeight) && double.IsFinite(partHeight))
            {
                job.PartHeight = partHeight;
            }
            else if (root["partHeight"] is not null)
            {
                warnings.Add("Altura da peça do arquivo ignorada (formato inválido): ficou vazia.");
            }

            if (root["stockShape"] is not null)
            {
                // formato novo estragado não impede de abrir: o bruto fica não definido, com aviso (nada suposto)
                if (StockShapeFrom(root["stockShape"]) is { } shape)
                {
                    job.SetStock(null, shape);
                }
                else
                {
                    warnings.Add("Bruto do arquivo ignorado (formato inválido): ficou como não definido.");
                    job.SetStock(null, null);
                }
            }

            if (root["fixture"] is JsonObject fx)
            {
                string Text(string key) => fx[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
                var fixture = new JobFixture(Text("kind"), Text("description").Trim());
                if (fixture.Validate() is { } bad)
                {
                    // arquivo com fixação estragada abre mesmo assim, sem inventar: fica "não definida"
                    warnings.Add($"Fixação do arquivo ignorada ({bad}): ficou como não definida.");
                }
                else
                {
                    job.Fixture = fixture;
                }
            }
            else if (root["fixture"] is not null)
            {
                warnings.Add("Fixação do arquivo ignorada (formato inválido): ficou como não definida.");
            }

            IReadOnlyList<string> problems = job.Validate();
            return problems.Count > 0 ? new JobLoadResult(null, problems[0]) : new JobLoadResult(job, null) { Warnings = warnings };
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
            var face = new RotaryFaceSpec(Str(f, "axis"), Num(f, "angle"), Num(f, "axisToFace"));
            spec = spec with { Face = f["axis2"] is null ? face : face with { Axis2 = Str(f, "axis2"), Angle2 = Num(f, "angle2") } };
        }

        return spec;
    }

    private static OpOrigin? OriginFrom(JsonNode? node)
    {
        if (node is not JsonObject g || g["source"] is not JsonValue sv || !sv.TryGetValue(out string? source) || source is null
            || g["typed"] is not JsonArray ta)
        {
            return null;
        }

        string? cut = null;
        if (g["cutData"] is { } cn && (cn is not JsonValue cv || !cv.TryGetValue(out cut)))
        {
            return null;
        }

        var typed = new List<string>();
        foreach (JsonNode? t in ta)
        {
            if (t is not JsonValue tv || !tv.TryGetValue(out string? s) || s is null)
            {
                return null;
            }

            typed.Add(s);
        }

        return new OpOrigin(source, cut, typed);
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
