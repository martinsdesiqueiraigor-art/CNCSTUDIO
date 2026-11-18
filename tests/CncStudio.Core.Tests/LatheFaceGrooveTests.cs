using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T2: canal de face (axial) em G0/G1, igual nos dois controles. Conteúdo técnico em rascunho, nunca rodado em máquina.</summary>
public class LatheFaceGrooveTests
{
    private static readonly LatheTool Tool = new(5, "Bedame de face 3", 120, 2000, 0);

    // canal Ø24 a Ø40, 5 mm de fundo, bedame de 3 mm (faixa Ø30 a Ø60)
    private static FaceGroovingParams P() => new(40, 24, 0, -5, 3, 2, 0.5, 0.08, 0.04, 30, 0.1, 30, 60, 2);

    private static LatheJob Job(CamDialect dialect, FaceGroovingParams? p = null)
    {
        var job = new LatheJob { Name = "CANAL FACE", Dialect = dialect };
        job.Operations.Add(new LatheOperationSpec(LatheOpKind.FaceGrooving, Tool) { FaceGroove = p ?? P() });
        return job;
    }

    [Fact]
    public void Percurso_fica_dentro_do_canal_e_chega_ao_fundo()
    {
        LatheResult r = LatheFaceGrooving.Build(Tool, 50, 40, P());
        Assert.True(r.Ok, r.Error);
        List<LatheMove> cuts = r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed).ToList();
        Assert.NotEmpty(cuts);
        // X do programa = aresta de maior Ø: de rIn + largura (15) a rOut (20)
        Assert.All(cuts, m => Assert.InRange(m.R, 15 - 1e-9, 20 + 1e-9));
        Assert.All(cuts, m => Assert.InRange(m.Z, -5 - 1e-9, 0));
        Assert.Equal(-5, cuts.Min(m => m.Z), 6);
        Assert.Equal(20, cuts.Max(m => m.R), 6);
        Assert.Equal(15, cuts.Min(m => m.R), 6);
        // nenhum rápido dentro do material fora da coluna já aberta: rápido abaixo da face só no mesmo X do corte anterior
        for (int i = 1; i < r.Operation.Moves.Count; i++)
        {
            LatheMove m = r.Operation.Moves[i], before = r.Operation.Moves[i - 1];
            if (m.Kind == MoveKind.Rapid && m.Z < 0)
            {
                Assert.Equal(before.R, m.R, 9);
            }
        }

        Assert.Equal([LatheFaceGrooving.Warning], r.Operation.Warnings);
    }

    [Fact]
    public void Desbaste_deixa_o_sobremetal_e_o_acabamento_tira()
    {
        LatheResult r = LatheFaceGrooving.Build(Tool, 50, 40, P());
        List<LatheMove> moves = r.Operation!.Moves.ToList();
        // primeiro mergulho: parede externa com sobremetal (X 39,8) e fundo com sobremetal (Z -4,9), em passes de 2 mm
        List<LatheMove> firstColumn = moves.Where(m => m.Kind == MoveKind.Feed && Math.Abs(m.R - 19.9) < 1e-9).ToList();
        Assert.Equal([-2, -4, -4.9], firstColumn.Select(m => Math.Round(m.Z, 6)));
        // sem sobremetal: uma passada só, direto nas paredes e no fundo
        LatheResult plain = LatheFaceGrooving.Build(Tool, 50, 40, P() with { Allowance = 0 });
        Assert.Equal(-5, plain.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed).Min(m => m.Z), 6);
        Assert.Contains(plain.Operation.Moves, m => m.Kind == MoveKind.Feed && Math.Abs(m.R - 20) < 1e-9);
    }

    [Fact]
    public void Avanco_reduzido_vale_do_diametro_digitado_para_baixo()
    {
        LatheResult r = LatheFaceGrooving.Build(Tool, 50, 40, P());
        foreach (LatheMove m in r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed))
        {
            // os cortes que partem de um X no Ø 30 ou menor usam o reduzido
            bool low = 2 * m.R <= 30 + 1e-9;
            if (low)
            {
                Assert.Equal(0.04, m.Feed);
            }
        }

        Assert.Contains(r.Operation.Moves, m => m.Kind == MoveKind.Feed && m.Feed == 0.04);
        Assert.Contains(r.Operation.Moves, m => m.Kind == MoveKind.Feed && m.Feed == 0.08);
        // 0 = nenhum mergulho com avanço reduzido
        LatheResult never = LatheFaceGrooving.Build(Tool, 50, 40, P() with { ReducedFromDiameter = 0 });
        Assert.All(never.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed), m => Assert.Equal(0.08, m.Feed));
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Programa_sai_em_G0_G1_com_o_aviso_e_o_limite_de_rotacao(CamDialect dialect)
    {
        LatheBuild build = LatheJobBuilder.Build(Job(dialect));
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        string program = build.Program;
        Assert.Contains("SIMULE ANTES DE RODAR NA MAQUINA", program);
        Assert.Contains(dialect == CamDialect.Fanuc ? "(Canal de face)" : "; Canal de face", program);
        Assert.Contains(dialect == CamDialect.Fanuc ? "G50 S2000" : "LIMS=2000", program);
        Assert.DoesNotContain("G71 U", program);
        Assert.DoesNotContain("CYCLE", program);
        // comentário sem acento e número com ponto decimal
        Assert.All(program.Where(c => c > 127), c => Assert.Fail($"caractere fora do ASCII no programa: {c}"));
        Assert.DoesNotContain(",", program.Replace("; ", "").Replace("LIMS", ""));
        Assert.Contains(dialect == CamDialect.Fanuc ? "G1 X39.8 Z-2. F0.08" : "G1 X39.8 Z-2 F0.08", program);
        Assert.Contains(LatheFaceGrooving.Warning, build.Warnings);
    }

    [Fact]
    public void Fanuc_e_Siemens_fazem_os_mesmos_movimentos()
    {
        static List<(bool Rapid, double X, double Z)> Path(CamDialect d)
        {
            string program = LatheJobBuilder.Build(Job(d)).Program;
            var path = MachineExecutor.Execute(GCodeParser.Parse(program, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings { Machine = MachineKind.Lathe });
            return path.Moves.Select(m => (m.IsRapid, Math.Round(m.End.X, 4), Math.Round(m.End.Z, 4))).ToList();
        }

        List<(bool Rapid, double X, double Z)> fanuc = Path(CamDialect.Fanuc);
        Assert.Equal(fanuc, Path(CamDialect.Siemens));
        // o simulador do Code lê o programa: corta do Ø 30 ao Ø 40 (raio 15 a 20) até Z -5
        var cuts = fanuc.Where(m => !m.Rapid).ToList();
        Assert.Equal(-5, cuts.Min(m => m.Z), 3);
        Assert.Equal(20, cuts.Max(m => m.X), 3);
        Assert.Equal(15, cuts.Min(m => m.X), 3);
    }

    [Fact]
    public void Campo_vazio_deixa_incompleta_e_tudo_digitado_gera()
    {
        LatheOperationSpec blank = LatheJobInfo.NewBlank(LatheOpKind.FaceGrooving, false);
        Assert.Contains(LatheJobInfo.FaceOuterField, blank.MissingFields);
        Assert.Contains(LatheJobInfo.FaceToolMaxField, blank.MissingFields);
        Assert.Contains(LatheJobInfo.ReducedFeedField, blank.MissingFields);
        Assert.Contains(LatheJobInfo.MaxRpmField, blank.MissingFields);
        Assert.Equal("Canal de face", LatheJobInfo.KindName(blank));
        // a operação nova pelo caminho antigo também nasce vazia (não há valor de fábrica)
        Assert.Equal(blank.MissingFields, LatheJobInfo.NewOperation(LatheOpKind.FaceGrooving, false).MissingFields);

        LatheJob job = Job(CamDialect.Fanuc);
        job.Operations[0] = LatheJobInfo.SetMissing(job.Operations[0], LatheJobInfo.FaceInnerField, true);
        LatheBuild bad = LatheJobBuilder.Build(job);
        Assert.False(bad.Ok);
        Assert.Equal(["Operação 1: Falta Ø interno do canal"], bad.Errors);

        job.Operations[0] = LatheJobInfo.SetMissing(job.Operations[0], LatheJobInfo.FaceInnerField, false);
        Assert.True(LatheJobBuilder.Build(job).Ok);
    }

    [Fact]
    public void Erros_de_geometria_e_da_faixa_do_bedame()
    {
        string? E(FaceGroovingParams p) => LatheFaceGrooving.Build(Tool, 50, 40, p).Error;
        Assert.Contains("maior que o Ø interno", E(P() with { InnerDiameter = 40 }));
        Assert.Contains("Z do fundo", E(P() with { BottomZ = 0 }));
        Assert.Contains("mais larga que o canal", E(P() with { ToolWidth = 9 }));
        Assert.Contains("faixa de Ø do bedame", E(P() with { ToolMaxDiameter = 30 }));
        Assert.Contains("fora da faixa do bedame", E(P() with { ToolMinDiameter = 45 }));
        Assert.Contains("fora da faixa do bedame", E(P() with { ToolMaxDiameter = 38 }));
        Assert.Contains("maiores que zero", E(P() with { ReducedFeed = 0 }));
        Assert.Contains("maiores que zero", E(P() with { Peck = 0 }));
        // limite de rotação obrigatório (G96 sem limite não gera)
        Assert.Contains("rotação máxima", LatheFaceGrooving.Build(Tool with { MaxRpm = 0 }, 50, 40, P()).Error);
        // largura exata: um mergulho só, sem acabamento
        LatheResult exact = LatheFaceGrooving.Build(Tool, 50, 40, P() with { ToolWidth = 8 });
        Assert.True(exact.Ok, exact.Error);
        Assert.All(exact.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed), m => Assert.Equal(20, m.R, 9));
    }

    [Fact]
    public void Salvar_e_abrir_devolve_a_mesma_operacao_e_o_botao_da_faixa_cria_ela()
    {
        LatheJob job = Job(CamDialect.Siemens);
        string json = LatheJobJson.ToJson(job);
        Assert.Contains("\"faceGroove\"", json);
        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(job.Operations[0].FaceGroove, back.Job!.Operations[0].FaceGroove);
        Assert.Equal(json, LatheJobJson.ToJson(back.Job));
        Assert.Equal(LatheJobBuilder.Build(job).Program, LatheJobBuilder.Build(back.Job).Program);

        CamPaletteItem item = CamPalette.LatheGroups.SelectMany(g => g.Items).Single(i => i.Id == "canal-face");
        Assert.False(item.Soon);
        Assert.Null(CamPalette.LatheUnavailable(item, new LatheJob()));
        LatheOperationSpec made = CamPalette.NewLatheOperation(item)!;
        Assert.Equal(LatheOpKind.FaceGrooving, made.Kind);
        Assert.Same(item, CamPalette.LatheItemFor(made));
        // largura do bedame do banco entra no campo e tira a marca de vazio
        LatheOperationSpec withWidth = LatheJobInfo.WithToolWidth(made, 3);
        Assert.Equal(3, withWidth.FaceGroove!.ToolWidth);
        Assert.DoesNotContain(LatheJobInfo.ToolWidthField, withWidth.MissingFields);
    }
}
