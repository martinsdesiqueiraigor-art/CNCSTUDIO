using System.Globalization;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>
/// CAM rápido para a web (celular): uma forma (retângulo ou círculo), uma operação de fresa e, se pedido, face girada no 4º eixo.
/// Mesmo cálculo do CAM do desktop; a tela é um formulário em HTML. Rascunho: nada foi testado em máquina.
/// </summary>
public static class CamQuickJson
{
    private static readonly string[] Shapes = ["Retângulo", "Círculo"];
    private static readonly string[] Operations = ["Contorno por fora", "Contorno por dentro", "Contorno sobre a linha", "Bolsão", "Texto enrolado no cilindro (4º eixo contínuo)"];
    private static readonly string[] Dialects = ["Fanuc", "Siemens"];
    private static readonly string[] Axes = ["A (gira em torno de X)", "B (gira em torno de Y)"];

    /// <summary>Campos do formulário: [{id,group,label,unit,kind:"num"|"select"|"check"|"text",def,options,hint,when}]. "when": condição de aparecer: grupos separados por "|" (ou), cada um com termos "id", "!id", "id=valor" ou "id!=valor" separados por "&" (e).</summary>
    public static string Catalog()
    {
        var a = new JsonArray();
        void Add(string group, string id, string label, string kind, JsonNode? def, string unit = "", string[]? options = null, string hint = "", string when = "")
        {
            var o = new JsonObject { ["group"] = group, ["id"] = id, ["label"] = label, ["kind"] = kind, ["def"] = def, ["unit"] = unit, ["hint"] = hint, ["when"] = when };
            if (options is not null)
            {
                var arr = new JsonArray();
                foreach (string s in options)
                {
                    arr.Add(JsonValue.Create(s));
                }

                o["options"] = arr;
            }

            a.Add(o);
        }

        const string G1 = "Peça e operação", G2 = "Ferramenta", G3 = "Alturas (Z = 0 no topo)", G4 = "4º eixo (mesa, modo calculado)", G5 = "Texto no cilindro";
        Add(G1, "dialect", "Comando", "select", 0, options: Dialects);
        Add(G1, "op", "Operação", "select", 0, options: Operations);
        Add(G1, "shape", "Forma", "select", 0, options: Shapes, when: "op!=4");
        Add(G1, "RX", "Retângulo: canto X", "num", 0, "mm", when: "shape=0&op!=4");
        Add(G1, "RY", "Retângulo: canto Y", "num", 0, "mm", when: "shape=0&op!=4");
        Add(G1, "RW", "Largura", "num", 60, "mm", when: "shape=0&op!=4");
        Add(G1, "RH", "Altura", "num", 40, "mm", when: "shape=0&op!=4");
        Add(G1, "CX", "Círculo: centro X", "num", 30, "mm", when: "shape=1&op!=4");
        Add(G1, "CY", "Círculo: centro Y", "num", 20, "mm", when: "shape=1&op!=4");
        Add(G1, "CD", "Diâmetro", "num", 30, "mm", when: "shape=1&op!=4");
        Add(G1, "STEP", "Bolsão: passo lateral", "num", 60, "% do diâmetro", when: "op=3");
        Add(G5, "TX", "Texto", "text", "CASILLAS", hint: "Letras maiúsculas sem acento, números e . , : ! - _ = + / ( ).", when: "op=4");
        Add(G5, "TH", "Altura das letras (ao redor)", "num", 6, "mm", when: "op=4");
        Add(G5, "TDIA", "Diâmetro da peça", "num", 40, "mm", when: "op=4");
        Add(G5, "TAX", "Início ao longo do eixo (X ou Y)", "num", 0, "mm", when: "op=4");
        Add(G5, "TANG", "Ângulo inicial", "num", 0, "°", when: "op=4");
        Add(G5, "TDEP", "Profundidade", "num", 0.3, "mm", when: "op=4");
        Add(G5, "TPASS", "Passe em profundidade", "num", 0.15, "mm", when: "op=4");
        Add(G5, "TCLR", "Folga acima da peça", "num", 3, "mm", when: "op=4", hint: "Ferramenta fina na vertical, sobre a linha de centro. O Z do centro do eixo (campo abaixo) é do zero da peça: com o zero no topo do cilindro, ele vale menos o raio.");
        Add(G2, "T", "Número da ferramenta (T)", "num", 1);
        Add(G2, "D", "Diâmetro", "num", 10, "mm");
        Add(G2, "S", "Rotação", "num", 3000, "rpm");
        Add(G2, "F", "Avanço no plano (na superfície, no cilindro)", "num", 500, "mm/min");
        Add(G2, "FZ", "Avanço de mergulho", "num", 200, "mm/min");
        Add(G3, "SAFE", "Altura segura", "num", 50, "mm", when: "op!=4");
        Add(G3, "CLR", "Aproximação", "num", 2, "mm", when: "op!=4");
        Add(G3, "TOP", "Topo", "num", 0, "mm", when: "op!=4");
        Add(G3, "BOT", "Fundo", "num", -6, "mm", when: "op!=4");
        Add(G3, "Q", "Passe em Z", "num", 3, "mm", when: "op!=4");
        Add(G4, "ROT", "Face girada no 4º eixo", "check", false, hint: "Desenhe a face de cima (Z 0 na face). O CAM leva o percurso para a face girada. Rode primeiro no ar.", when: "op!=4");
        Add(G4, "AXIS", "Eixo rotativo", "select", 0, options: Axes, when: "ROT&op!=4|op=4");
        Add(G4, "ANG", "Ângulo da face", "num", 90, "°", when: "ROT&op!=4");
        Add(G4, "DIST", "Do centro do eixo até a face (virada para cima)", "num", 25, "mm", when: "ROT&op!=4");
        Add(G4, "PIVH", "Centro do eixo na horizontal (A: Y, B: X; do zero da peça)", "num", 0, "mm", when: "ROT&op!=4|op=4");
        Add(G4, "PIVZ", "Centro do eixo em Z (do zero da peça)", "num", 0, "mm", when: "ROT&op!=4|op=4");
        Add(G4, "MINA", "Curso mínimo", "num", -360, "°", when: "ROT&op!=4|op=4");
        Add(G4, "MAXA", "Curso máximo", "num", 360, "°", when: "ROT&op!=4|op=4");
        Add(G4, "REV", "Sentido de giro invertido", "check", false, hint: "Marque se, no ar, a mesa girar ao contrário.", when: "ROT&op!=4|op=4");
        return a.ToJsonString();
    }

    /// <summary>Gera o programa. Devolve {"ok":bool,"program":"…","errors":[…],"warnings":[…]}.</summary>
    public static string Generate(string valuesJson)
    {
        try
        {
            if (JsonNode.Parse(valuesJson) is not JsonObject v)
            {
                return Result(false, "", ["Valores inválidos."], []);
            }

            double Num(string id) => v[id] is JsonValue n && n.TryGetValue(out double d) && double.IsFinite(d) ? d : double.NaN;
            bool Chk(string id) => v[id] is JsonValue n && n.TryGetValue(out bool b) && b;
            int Sel(string id) => v[id] is JsonValue n && n.TryGetValue(out double d) ? (int)d : 0;

            bool wrap = Sel("op") == 4;
            string[] needed = wrap ? ["T", "D", "S", "F", "FZ"] : ["T", "D", "S", "F", "FZ", "SAFE", "CLR", "TOP", "BOT", "Q"];
            if (needed.Any(id => double.IsNaN(Num(id))))
            {
                return Result(false, "", ["Há um campo da ferramenta ou das alturas vazio ou inválido."], []);
            }

            var tool = new CamTool((int)Num("T"), Num("D"), Num("S"), Num("F"), Num("FZ"));
            if (tool.Validate() is { } toolError)
            {
                return Result(false, "", [toolError], []);
            }

            if (wrap)
            {
                return GenerateWrap(v, tool);
            }

            var levels = new CamLevels(Num("SAFE"), Num("CLR"), Num("TOP"), Num("BOT"), Num("Q"));
            if (levels.Validate() is { } levelError)
            {
                return Result(false, "", [levelError], []);
            }

            Contour part;
            if (Sel("shape") == 0)
            {
                double[] r = [Num("RX"), Num("RY"), Num("RW"), Num("RH")];
                if (r.Any(double.IsNaN) || r[2] <= 0 || r[3] <= 0)
                {
                    return Result(false, "", ["Retângulo: preencha os campos; largura e altura maiores que zero."], []);
                }

                part = Contour.Rectangle(r[0], r[1], r[2], r[3]);
            }
            else
            {
                double[] c = [Num("CX"), Num("CY"), Num("CD")];
                if (c.Any(double.IsNaN) || c[2] <= 0)
                {
                    return Result(false, "", ["Círculo: preencha os campos; diâmetro maior que zero."], []);
                }

                part = Contour.Circle(new Point2(c[0], c[1]), c[2] / 2);
            }

            int op = Sel("op");
            OperationResult result = op switch
            {
                0 => CamOperations.Contour(part, tool, levels, new ContourParams(ContourSide.Outside)),
                1 => CamOperations.Contour(part, tool, levels, new ContourParams(ContourSide.Inside)),
                2 => CamOperations.Contour(part, tool, levels, new ContourParams(ContourSide.OnLine)),
                _ => CamOperations.Pocket(part, tool, levels, new PocketParams(double.IsNaN(Num("STEP")) ? 60 : Num("STEP"))),
            };
            if (!result.Ok)
            {
                return Result(false, "", [result.Error ?? "Não foi possível gerar a operação."], []);
            }

            if (Chk("ROT"))
            {
                double[] r = [Num("ANG"), Num("DIST"), Num("PIVH"), Num("PIVZ"), Num("MINA"), Num("MAXA")];
                if (r.Any(double.IsNaN))
                {
                    return Result(false, "", ["4º eixo: há um campo vazio ou inválido."], []);
                }

                string letter = Sel("AXIS") == 1 ? "B" : "A";
                var axis = new RotaryAxisConfig
                {
                    Letter = letter,
                    Carrier = RotaryCarrier.Table,
                    MinAngle = r[4],
                    MaxAngle = r[5],
                    Reversed = Chk("REV"),
                    PivotY = letter == "A" ? r[2] : 0,
                    PivotX = letter == "B" ? r[2] : 0,
                    PivotZ = r[3],
                };
                var machine = new MachineProfile { Name = "Web", Controller = Sel("dialect") == 1 ? CamDialect.Siemens : CamDialect.Fanuc, Axes = [axis] };
                List<string> problems = machine.Validate();
                if (problems.Count > 0)
                {
                    return Result(false, "", problems, []);
                }

                result = RotaryIndexing.Place(result.Operation!, machine, letter, new IndexedFace(r[0], r[1]));
                if (!result.Ok)
                {
                    return Result(false, "", [result.Error ?? "Face girada não pôde ser gerada."], []);
                }
            }

            CamOperation operation = result.Operation!;
            CamDialect dialect = Sel("dialect") == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
            string program = CamPost.Generate([operation], new PostOptions(dialect, ProgramName: "CASILLAS CAM"));
            var warnings = operation.Warnings.ToList();
            warnings.AddRange(CamPost.Check([operation]));
            return Result(true, program, [], warnings);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return Result(false, "", ["Valores inválidos."], []);
        }
    }

    private static string GenerateWrap(JsonObject v, CamTool tool)
    {
        double Num(string id) => v[id] is JsonValue n && n.TryGetValue(out double d) && double.IsFinite(d) ? d : double.NaN;
        int Sel(string id) => v[id] is JsonValue n && n.TryGetValue(out double d) ? (int)d : 0;
        bool Chk(string id) => v[id] is JsonValue n && n.TryGetValue(out bool b) && b;

        double[] f = [Num("TH"), Num("TDIA"), Num("TAX"), Num("TANG"), Num("TDEP"), Num("TPASS"), Num("TCLR"), Num("PIVH"), Num("PIVZ"), Num("MINA"), Num("MAXA")];
        if (f.Any(double.IsNaN))
        {
            return Result(false, "", ["Texto no cilindro ou 4º eixo: há um campo vazio ou inválido."], []);
        }

        string text = v["TX"] is JsonValue tv && tv.TryGetValue(out string? raw) ? raw ?? "" : "";
        string normalized = Generators.StrokeFont.Normalize(text).Trim();
        if (normalized.Length == 0)
        {
            return Result(false, "", ["Texto: digite o que será gravado."], []);
        }

        if (normalized.Length > 60)
        {
            return Result(false, "", ["Texto: no máximo 60 caracteres."], []);
        }

        var bad = normalized.Where(c => !Generators.StrokeFont.TryGet(c, out _)).Distinct().ToList();
        if (bad.Count > 0)
        {
            return Result(false, "", ["Texto: não consigo desenhar " + string.Join(" ", bad.Select(c => $"\"{c}\"")) + ". Aceito: " + Generators.StrokeFont.Supported + "."], []);
        }

        if (f[0] <= 0)
        {
            return Result(false, "", ["Altura das letras precisa ser maior que zero."], []);
        }

        string letter = Sel("AXIS") == 1 ? "B" : "A";
        var axis = new RotaryAxisConfig
        {
            Letter = letter,
            Carrier = RotaryCarrier.Table,
            MinAngle = f[9],
            MaxAngle = f[10],
            Reversed = Chk("REV"),
            PivotY = letter == "A" ? f[7] : 0,
            PivotX = letter == "B" ? f[7] : 0,
            PivotZ = f[8],
        };
        CamDialect dialect = Sel("dialect") == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        var machine = new MachineProfile { Name = "Web", Controller = dialect, Axes = [axis] };
        List<string> problems = machine.Validate();
        if (problems.Count > 0)
        {
            return Result(false, "", problems, []);
        }

        var strokes = RotaryWrap.TextStrokes(normalized, f[0]);
        var setup = new WrapSetup(f[1], f[2], f[3], f[4], f[5], f[6], Num("F"), Num("FZ"));
        OperationResult result = RotaryWrap.Engrave("Texto no cilindro", tool, machine, letter, strokes, setup);
        if (!result.Ok)
        {
            return Result(false, "", [result.Error ?? "Não foi possível gerar a gravação."], []);
        }

        CamOperation operation = result.Operation!;
        string program = CamPost.Generate([operation], new PostOptions(dialect, ProgramName: "CASILLAS CAM"));
        var warnings = operation.Warnings.ToList();
        warnings.AddRange(CamPost.Check([operation]));
        return Result(true, program, [], warnings);
    }

    /// <summary>Linhas de giro do último formulário (para o 3D desfazer o giro): null se a face girada não estava marcada.</summary>
    public static Scene.RotaryPartFrame? PartFrameOf(string valuesJson)
    {
        try
        {
            if (JsonNode.Parse(valuesJson) is not JsonObject v
                || !((v["ROT"] is JsonValue r && r.TryGetValue(out bool on) && on) || (v["op"] is JsonValue o && o.TryGetValue(out double op) && (int)op == 4)))
            {
                return null;
            }

            double Num(string id) => v[id] is JsonValue n && n.TryGetValue(out double d) && double.IsFinite(d) ? d : 0;
            bool b = v["AXIS"] is JsonValue ax && ax.TryGetValue(out double axis) && (int)axis == 1;
            bool rev = v["REV"] is JsonValue rv && rv.TryGetValue(out bool reversed) && reversed;
            return b
                ? new Scene.RotaryPartFrame(0, 0, false, Num("PIVH"), Num("PIVZ"), rev)
                : new Scene.RotaryPartFrame(Num("PIVH"), Num("PIVZ"), rev, 0, 0, false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Result(bool ok, string program, IEnumerable<string> errors, IEnumerable<string> warnings)
    {
        var e = new JsonArray();
        foreach (string s in errors)
        {
            e.Add(JsonValue.Create(s));
        }

        var w = new JsonArray();
        foreach (string s in warnings)
        {
            w.Add(JsonValue.Create(s));
        }

        return new JsonObject { ["ok"] = ok, ["program"] = program, ["errors"] = e, ["warnings"] = w }.ToJsonString();
    }

}
