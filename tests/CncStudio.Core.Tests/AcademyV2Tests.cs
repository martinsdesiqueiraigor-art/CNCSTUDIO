using System.Reflection;
using System.Text.Json.Nodes;
using CncStudio.Core.Academy;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>ACAD-03 passo 1: JSON v2 da aula, dialeto e máquina da aula, plano em JSON, conversão v1 → v2, quiz corrigido no Core e progresso com versão.</summary>
[Collection("AcademyJson")]
public class AcademyV2Tests
{
    private const string V2Lesson = """
        {
          "versao": 2, "id": "t-a01", "modulo": "acad-m6", "titulo": "Primeiro passe", "nivel": "Iniciante", "caminho": "Torno",
          "dialeto": "siemens", "maquina": "torno", "estado": "rascunho", "objetivo": "Ler um passe simples.",
          "bruto": { "tipo": "barra", "diametro": 30, "comprimento": 50 },
          "secoes": {
            "visao": { "estado": "rascunho", "texto": "Texto da visão geral.", "conceitos": ["um", "dois", "três"], "imagem": "torno.svg" },
            "trajetoria": { "estado": "rascunho", "programa": "samples/academy/t.mpf" },
            "sintaxe": { "estado": "revisado", "blocos": ["G1 X.. Z.. F.."], "parametros": [{ "endereco": "X", "conceito": "diâmetro" }] },
            "exemplo": { "estado": "rascunho", "programa": "samples/academy/t.mpf", "etapas": ["aproxima", "corta"] },
            "cuidados": { "estado": "rascunho", "erros": ["Esquecer o G96."], "alertas": ["Simule antes de rodar na máquina."] },
            "praticar": { "estado": "rascunho", "exercicio": { "tipo": "corrigir", "enunciado": "Corrija.", "gabarito": "G1 X20" }, "quiz": [] }
          }
        }
        """;

    private static readonly Dictionary<string, string> Programs = new()
    {
        ["samples/academy/t.mpf"] = "; SIMULE ANTES DE RODAR NA MAQUINA\nG0 X32 Z2\nG1 X20 F0.2\nG1 Z-20\nG0 X40\nM30\n",
    };

    [Fact]
    public void Leitor_aceita_v2_e_preenche_os_campos_que_a_tela_atual_le()
    {
        Lesson l = AcademyReader.ReadLesson(V2Lesson, out string err)!;
        Assert.Equal("", err);
        Assert.Equal(2, l.Version);
        Assert.Equal(("siemens", "torno"), (l.Dialect, l.Machine));
        Assert.Equal("Texto da visão geral.", l.Text);
        Assert.Equal("samples/academy/t.mpf", l.ProgramPath);
        Assert.Equal("Esquecer o G96.", l.CommonMistake);
        Assert.Contains("Simule antes", l.Warning);
        Assert.Equal(["um", "dois", "três"], l.Summary);
        Assert.Equal("revisado", l.Sections!.Status["sintaxe"]);
        Assert.Equal(new SyntaxParam("X", "diâmetro"), l.Sections.SyntaxParams[0]);
        Assert.Equal(["aproxima", "corta"], l.Sections.ExampleSteps);
        Assert.Equal(30, l.Stock!.Diameter);
        Assert.Equal("G1 X20", l.Exercise!.Answer);
    }

    [Fact]
    public void Aula_v1_continua_v1_fanuc_e_fresa()
    {
        AcademyLibrary lib = AcademyLibrary.FromEmbedded();
        Lesson l = lib.FindLesson("acad-m1-a01")!;
        Assert.Equal((1, "fanuc", "fresa", (LessonSections?)null), (l.Version, l.Dialect, l.Machine, l.Sections));
        Assert.DoesNotContain(lib.Validate(), i => i.IsError);
    }

    [Fact]
    public void Validador_v2_exige_dialeto_maquina_e_estado_das_secoes()
    {
        Lesson ok = AcademyReader.ReadLesson(V2Lesson, out _)!;
        List<ContentIssue> issues = AcademyValidator.Validate([ok], null, Programs);
        Assert.DoesNotContain(issues, i => i.Message.Contains("dialeto") || i.Message.Contains("maquina") || i.Message.Contains("estado"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("t.mpf") && i.IsError);

        JsonObject bad = JsonNode.Parse(V2Lesson)!.AsObject();
        bad["dialeto"] = "heidenhain";
        bad["maquina"] = "";
        bad["secoes"]!["cuidados"]!.AsObject().Remove("estado");
        bad["secoes"]!["trajetoria"]!["programa"] = "samples/academy/nao-existe.nc";
        bad["secoes"]!["sintaxe"] = new JsonObject { ["estado"] = "rascunho", ["blocos"] = new JsonArray(), ["parametros"] = new JsonArray() };
        List<ContentIssue> found = AcademyValidator.Validate([AcademyReader.ReadLesson(bad.ToJsonString(), out _)!], null, Programs);
        Assert.Contains(found, i => i.IsError && i.Message.Contains("dialeto \"heidenhain\""));
        Assert.Contains(found, i => i.IsError && i.Message.Contains("maquina"));
        Assert.Contains(found, i => i.IsError && i.Message.Contains("\"cuidados\" sem \"estado\""));
        Assert.Contains(found, i => i.IsError && i.Message.Contains("nao-existe.nc"));
        Assert.Contains(found, i => !i.IsError && i.Message.Contains("\"sintaxe\" vazia"));
    }

    [Fact]
    public void Leitura_e_conferencia_usam_a_maquina_da_aula()
    {
        // X em diâmetro no torno: o mesmo programa termina em outro X na fresa
        string program = Programs["samples/academy/t.mpf"];
        Walkthrough lathe = ProgramWalkthrough.Build(program, "siemens", "torno");
        Walkthrough mill = ProgramWalkthrough.Build(program, "siemens", "fresa");
        double LastX(Walkthrough w) => w.Lines.Last(l => l.Path.Count > 0).Path[^1].X;
        Assert.NotEqual(LastX(mill), LastX(lathe));

        var editor = new ExerciseEditor("", "", [new ExerciseCheck("sem_erros", "", null, null, null, "")]);
        Assert.True(ExerciseChecker.Check(editor, "G0 X10 ; comentário Siemens\nM30\n", "siemens").Passed);
    }

    [Fact]
    public void Plano_vem_do_json_e_modulo_novo_entra_sem_codigo()
    {
        Assert.Equal(["Comum", "Fresa", "Torno"], AcademyLibrary.Tracks);
        Assert.Equal(7, AcademyLibrary.Plan.Count);
        Assert.False(AcademyLibrary.Plan.Single(p => p.Id == "acad-m1").Soon);
        Assert.True(AcademyLibrary.Plan.Single(p => p.Id == "acad-m7").Soon);

        const string plan = """
            { "trilhas": ["Comum", "Torno"], "niveis": ["Introdução", "Avançado"],
              "modulos": [ { "id": "acad-m8", "titulo": "Torno: roscas", "trilha": "Torno", "nivel": "Avançado", "descricao": "G76", "em_breve": true } ] }
            """;
        AcademyPlan p = AcademyPlan.FromJson(plan, out string err)!;
        Assert.Equal("", err);
        Assert.Equal(new PlannedModule("acad-m8", "Torno: roscas", "Torno", "Avançado", "G76", true), p.Modules[0]);
        Assert.Null(AcademyPlan.FromJson(plan.Replace("\"Torno\", \"nivel\"", "\"Laser\", \"nivel\""), out string bad));
        Assert.Contains("acad-m8", bad);
    }

    [Fact]
    public void Conversao_v1_para_v2_dos_modulos_1_e_2_passa_no_validador()
    {
        // a saída só vai para disco fora do repositório, se pedida por CASILLAS_ACADEMY_V2_OUT
        string? outDir = Environment.GetEnvironmentVariable("CASILLAS_ACADEMY_V2_OUT");
        AcademyLibrary v1 = AcademyLibrary.FromEmbedded();
        var files = new List<(string Name, string Text)>();
        int converted = 0;
        foreach ((string name, string text) in EmbeddedContent())
        {
            if (name.EndsWith(".json", StringComparison.Ordinal) && !name.EndsWith("-teste-final.json", StringComparison.Ordinal))
            {
                string v2 = AcademyConverter.ToV2(text, out string err)!;
                Assert.Equal("", err);
                files.Add((name, v2));
                converted++;
                if (outDir is not null)
                {
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, name), v2);
                }

                Lesson before = AcademyReader.ReadLesson(text, out _)!;
                Lesson after = AcademyReader.ReadLesson(v2, out _)!;
                Assert.Equal(2, after.Version);
                Assert.Equal(("fanuc", "fresa"), (after.Dialect, after.Machine));
                Assert.Equal(before.ProgramPath, after.ProgramPath);
                Assert.Equal(before.CommonMistake, after.CommonMistake);
                Assert.Equal(before.Warning, after.Warning);
                Assert.Equal(before.Summary, after.Summary);
                Assert.Equal(before.Quiz.Select(Key), after.Quiz.Select(Key));
                Assert.Equal(before.Exercise?.Answer, after.Exercise?.Answer);
                Assert.Equal(before.Text, after.Text);
                Assert.Equal(before.Opening, after.Opening);
                Assert.Equal("", after.Sections!.TrajectoryPath);
                if (before.Version == 1)
                {
                    // aula ainda v1: sintaxe vazia na conversão (o módulo 2 já está em v2 no repositório e volta igual)
                    Assert.Empty(after.Sections.SyntaxParams);
                }
            }
            else
            {
                files.Add((name, text));
            }
        }

        Assert.True(converted >= 10, $"só {converted} aulas convertidas");
        Assert.Contains(files, f => f.Name.StartsWith("acad-m2", StringComparison.Ordinal));
        AcademyLibrary v2Lib = AcademyLibrary.FromFiles(files);
        Assert.DoesNotContain(v2Lib.Validate(), i => i.IsError);
        Assert.Equal(v1.Module("acad-m2")!.Lessons.Count, v2Lib.Module("acad-m2")!.Lessons.Count);
        // só as seções que o v1 não tem viram aviso
        Assert.All(v2Lib.Validate().Where(i => !i.IsError && i.Message.Contains("vazia")), i => Assert.Matches("\"(trajetoria|sintaxe)\" vazia", i.Message));
    }

    [Fact]
    public void Piloto_modulo_2_em_v2_rascunho_com_sintaxe_so_na_a02()
    {
        AcademyLibrary lib = AcademyLibrary.FromEmbedded();
        Assert.DoesNotContain(lib.Validate(), i => i.IsError);
        Assert.All(lib.Module("acad-m1")!.Lessons, l => Assert.Equal(1, l.Version));
        AcademyModule m2 = lib.Module("acad-m2")!;
        Assert.Equal(6, m2.Lessons.Count);
        foreach (Lesson l in m2.Lessons)
        {
            Assert.Equal(2, l.Version);
            Assert.Equal("rascunho", l.Status);
            Assert.All(LessonSections.Names, n => Assert.Equal("rascunho", l.Sections!.Status[n]));
            Assert.NotEqual("", l.Opening);
            bool syntax = l.Sections!.SyntaxBlocks.Count > 0 || l.Sections.SyntaxParams.Count > 0;
            Assert.Equal(l.Id == "acad-m2-a02", syntax);
        }

        AcademyJson.UseLibrary(null);
        JsonObject a02 = JsonNode.Parse(AcademyJson.Dispatch("lesson", """{"id":"acad-m2-a02","hideAnswers":true}"""))!.AsObject();
        Assert.Equal("G01 X30.0 Y10.0 F120.0", (string)a02["sections"]!["syntaxBlocks"]![0]!);
        Assert.Equal("F", (string)a02["sections"]!["syntaxParams"]![2]!["address"]!);
        Assert.NotEqual("", (string)a02["opening"]!);
        JsonObject catalog = JsonNode.Parse(AcademyJson.Dispatch("catalog", "{}"))!.AsObject();
        IEnumerable<string> descriptions = catalog["tracks"]!.AsArray().First(t => (string)t!["name"]! == "Fresa")!["levels"]!.AsArray()
            .Select(l => l!["module"]?["description"]?.GetValue<string>() ?? "");
        Assert.Contains("ciclos, subprogramas e compensação", descriptions);
    }

    [Fact]
    public void Quiz_sem_gabarito_e_corrigido_pelo_core()
    {
        AcademyJson.UseLibrary(null);
        JsonObject open = JsonNode.Parse(AcademyJson.Dispatch("lesson", """{"id":"acad-m1-a01"}"""))!.AsObject();
        Assert.NotNull(open["quiz"]![0]!["correct"]);

        JsonObject hidden = JsonNode.Parse(AcademyJson.Dispatch("lesson", """{"id":"acad-m1-a01","hideAnswers":true}"""))!.AsObject();
        JsonObject q = hidden["quiz"]![0]!.AsObject();
        Assert.Null(q["correct"]);
        Assert.Null(q["explanation"]);
        JsonObject finalHidden = JsonNode.Parse(AcademyJson.Dispatch("final", """{"module":"acad-m1","seed":3,"hideAnswers":true}"""))!.AsObject();
        Assert.All(finalHidden["quiz"]!.AsArray(), x => Assert.Null(x!["correct"]));

        int right = (int)open["quiz"]![0]!["correct"]!;
        string id = (string)q["id"]!;
        JsonObject a = JsonNode.Parse(AcademyJson.Dispatch("answer", $$"""{"quizId":"acad-m1-a01-quiz","qid":"{{id}}","choice":{{right}}}"""))!.AsObject();
        Assert.True((bool)a["correct"]!);
        Assert.Equal(right, (int)a["right"]!);
        Assert.True(((string)a["explanation"]!).Length >= 20);

        var ids = new JsonArray(hidden["quiz"]!.AsArray().Select(x => (JsonNode)JsonValue.Create((string)x!["id"]!)).ToArray());
        string grade = AcademyJson.Dispatch("grade", new JsonObject { ["quizId"] = "acad-m1-a01-quiz", ["ids"] = ids, ["answers"] = new JsonArray(-1, -1, -1, -1) }.ToJsonString());
        JsonArray results = JsonNode.Parse(grade)!["results"]!.AsArray();
        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.False((bool)r!["ok"]!));
        Assert.Equal(right, (int)results[0]!["right"]!);
    }

    [Fact]
    public void Teste_final_real_e_corrigido_pelo_id_do_arquivo()
    {
        // o conteúdo real usa "acad-m1-final" (não "…-teste-final"); antes a correção devolvia ok:false
        AcademyJson.UseLibrary(null);
        JsonObject f = JsonNode.Parse(AcademyJson.Dispatch("final", """{"module":"acad-m1","seed":5,"hideAnswers":true}"""))!.AsObject();
        string quizId = (string)f["quizId"]!;
        JsonArray quiz = f["quiz"]!.AsArray();
        var ids = new JsonArray(quiz.Select(q => (JsonNode)JsonValue.Create((string)q!["id"]!)).ToArray());
        var answers = new JsonArray(quiz.Select(_ => (JsonNode)JsonValue.Create(0)).ToArray());
        JsonObject g = JsonNode.Parse(AcademyJson.Dispatch("grade", new JsonObject { ["quizId"] = quizId, ["ids"] = ids, ["answers"] = answers }.ToJsonString()))!.AsObject();
        Assert.True((bool)g["ok"]!);
        Assert.Equal(quiz.Count, g["results"]!.AsArray().Count);
        Assert.Equal((int)g["percent"]!, AcademyProgress.FromJson((string)g["progress"]!).BestScore(quizId));
        JsonObject a = JsonNode.Parse(AcademyJson.Dispatch("answer", $$"""{"quizId":"{{quizId}}","qid":"{{(string)quiz[0]!["id"]!}}","choice":0}"""))!.AsObject();
        Assert.True((bool)a["ok"]!);
    }

    [Fact]
    public void Progresso_antigo_migra_sem_perder_aulas_nem_notas()
    {
        const string old = """{"done":["acad-m1-a01","acad-m1-a02"],"scores":{"acad-m1-a01-quiz":75,"acad-m1-teste-final":90},"days":["2026-10-30"]}""";
        AcademyProgress p = AcademyProgress.FromJson(old);
        Assert.Equal(1, p.LoadedVersion);
        p.SetTab("acad-m1-a02", "exemplo");

        JsonObject saved = JsonNode.Parse(p.ToJson())!.AsObject();
        Assert.Equal(AcademyProgress.CurrentVersion, (int)saved["versao"]!);
        AcademyProgress again = AcademyProgress.FromJson(saved.ToJsonString());
        Assert.Equal(AcademyProgress.CurrentVersion, again.LoadedVersion);
        Assert.True(again.IsDone("acad-m1-a01") && again.IsDone("acad-m1-a02"));
        Assert.Equal(75, again.BestScore("acad-m1-a01-quiz"));
        Assert.Equal(90, again.BestScore("acad-m1-teste-final"));
        Assert.Equal(1, again.Streak("2026-10-30"));
        Assert.Equal("exemplo", again.Tabs["acad-m1-a02"]);
    }

    private static string Key(QuizQuestion q) => string.Join("|", [q.Id, q.Prompt, .. q.Options, q.CorrectIndex.ToString(), q.Explanation, q.Hint, q.Kind]);

    private static IEnumerable<(string Name, string Text)> EmbeddedContent()
    {
        Assembly asm = typeof(AcademyLibrary).Assembly;
        foreach (string res in asm.GetManifestResourceNames().Where(n => n.StartsWith("Academy.", StringComparison.Ordinal) || n.StartsWith("AcademyProgram.", StringComparison.Ordinal)))
        {
            using Stream s = asm.GetManifestResourceStream(res)!;
            using StreamReader reader = new(s);
            yield return (res[(res.IndexOf('.') + 1)..], reader.ReadToEnd());
        }
    }
}
