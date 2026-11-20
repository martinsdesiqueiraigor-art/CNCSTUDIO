using CncStudio.Core.Cam;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

/// <summary>OPS-A-02 C1: operação Chanfro (fresa em V): geometria, Incompleta, arquivo versão 7 e perfil cone no pacote.</summary>
public class CamChamferTests
{
    private static readonly CamTool V = new(7, 10, 8000, 800, 300, "Fresa V 90");
    private static readonly CamLevels Levels = new(50, 2, 0, -5, 2.5);
    private static readonly Contour Box = Contour.Rectangle(0, 0, 60, 40);

    private static CamOperation Ok(OperationResult r)
    {
        Assert.True(r.Ok, r.Error);
        return r.Operation!;
    }

    private static CamJob JobWith(CamOperationSpec spec)
    {
        var job = new CamJob { ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.Contours.Add(Box);
        job.Operations.Add(spec);
        return job;
    }

    // ------------------------------------------------ os `_ =>` que criariam a operação errada

    [Fact]
    public void Operacao_nova_de_chanfro_nao_vira_texto_no_cilindro()
    {
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0);
        Assert.Equal(JobOpKind.Chamfer, s.Kind);
        Assert.NotNull(s.Chamfer);
        Assert.Null(s.Wrap);
        Assert.Equal("Chanfro", CamJobInfo.KindName(s));
    }

    [Fact]
    public void Chanfro_entra_pelas_listas_e_pela_faixa()
    {
        Assert.Contains(CamJobInfo.NewKinds, k => k.Kind == JobOpKind.Chamfer);
        CamPaletteItem item = CamPalette.VisibleGroups.SelectMany(g => g.Items).Single(i => i.Id == "chanfro");
        Assert.False(item.Soon);
        Assert.Equal(JobOpKind.Chamfer, item.Kind);
        Assert.Equal(JobOpKind.Chamfer, CamPalette.NewOperation(item, 0)!.Kind);
        Assert.Equal("chanfro", CamPalette.ItemFor(CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0))!.Id);
        var open = new CamJob();
        open.Contours.Add(new Contour([Segment2.Line(new Point2(0, 0), new Point2(50, 0)), Segment2.Line(new Point2(50, 0), new Point2(50, 30))]));
        Assert.NotNull(CamPalette.Unavailable(item, open, 0));
    }

    [Fact]
    public void Validar_pega_chanfro_sem_dados_sem_contorno_e_sobre_a_linha()
    {
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0);
        Assert.Empty(JobWith(s).Validate());
        Assert.Contains("faltam os dados do chanfro", JobWith(s with { Chamfer = null }).Validate()[0]);
        Assert.Contains(JobWith(s with { ContourIndex = -1 }).Validate(), e => e.Contains("escolha um contorno"));
        Assert.Contains(JobWith(s with { Chamfer = s.Chamfer! with { Side = ContourSide.OnLine } }).Validate(), e => e.Contains("por fora ou por dentro"));
    }

    // ------------------------------------------------ Incompleta, nada suposto

    [Fact]
    public void Chanfro_da_faixa_nasce_incompleta_e_nao_gera()
    {
        CamOperationSpec s = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0));
        foreach (string f in new[] { CamJobInfo.TipAngleField, CamJobInfo.ChamferWidthField, CamJobInfo.ChamferDepthField, CamJobInfo.ChamferLeadField })
        {
            Assert.Contains(f, s.MissingFields);
        }

        JobBuild build = CamJobBuilder.Build(JobWith(s), null);
        Assert.False(build.Ok);
        Assert.Contains(CamJobInfo.TipAngleField, build.Errors[0]);
        Assert.Empty(CamJobInfo.SaveBlockers(JobWith(s)));
    }

    // ------------------------------------------------ geometria

    [Fact]
    public void Ponta_na_aresta_quando_a_profundidade_fecha_a_largura()
    {
        // V 90°: largura 1 pede profundidade 1 → eixo sobre a linha, ponta em Z -1
        CamOperation op = Ok(CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 90, 1, 1, 3)));
        Assert.Equal("Chanfro", op.Name);
        var cut = op.Moves.Where(m => m.Kind == MoveKind.Feed && m.Z < 0).ToList();
        Assert.All(cut, m => Assert.Equal(-1, m.Z, 6));
        Assert.Equal(0, cut.Min(m => m.X), 6);
        Assert.Equal(60, cut.Max(m => m.X), 6);
    }

    [Fact]
    public void Ponta_mais_funda_afasta_o_eixo_da_aresta_por_fora_e_por_dentro()
    {
        // V 90°, largura 1, profundidade 2 → o = 2·1 − 1 = 1 mm
        Assert.Equal(1.0, CamOperations.ChamferOffset(V, new ChamferParams(ContourSide.Outside, 90, 1, 2, 3), out string? e1)!.Value, 9);
        Assert.Null(e1);
        CamOperation outside = Ok(CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 90, 1, 2, 3)));
        CamOperation inside = Ok(CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Inside, 90, 1, 2, 3)));
        var outCut = outside.Moves.Where(m => m.Kind == MoveKind.Feed && m.Z < 0).ToList();
        var inCut = inside.Moves.Where(m => m.Kind == MoveKind.Feed && m.Z < 0).ToList();
        Assert.All(outCut, m => Assert.Equal(-2, m.Z, 6));
        Assert.Equal(-1, outCut.Min(m => m.X), 6);
        Assert.Equal(61, outCut.Max(m => m.X), 6);
        Assert.Equal(1, inCut.Min(m => m.X), 6);
        Assert.Equal(59, inCut.Max(m => m.X), 6);
    }

    [Fact]
    public void Z_da_ponta_segue_o_topo_da_peca()
    {
        CamOperation op = Ok(CamOperations.Chamfer(Box, V, Levels with { TopZ = 8, ClearanceZ = 10 }, new ChamferParams(ContourSide.Outside, 90, 1, 1, 3)));
        Assert.Equal(7, op.Moves.Where(m => m.Kind == MoveKind.Feed).Min(m => m.Z), 6);
    }

    [Fact]
    public void Numeros_que_nao_fecham_dao_erro_claro()
    {
        Assert.Contains("pelo menos", CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 90, 1, 0.5, 3)).Error);
        Assert.Contains("não alcança", CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 90, 1, 10, 3)).Error);
        Assert.Contains("ângulo", CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 180, 1, 1, 3)).Error);
        Assert.Contains("maiores que zero", CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 90, 0, 1, 3)).Error);
        Assert.Contains("entrada", CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 90, 1, 1, 0)).Error);
        Assert.NotNull(CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.OnLine, 90, 1, 1, 3)).Error);
    }

    [Fact]
    public void Angulo_diferente_muda_a_profundidade_necessaria()
    {
        // V 60°: tan 30° = 0,577; largura 1 pede profundidade ≥ 1,732
        Assert.Contains("pelo menos 1,732", CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 60, 1, 1.5, 3)).Error!.Replace('.', ','));
        Assert.True(CamOperations.Chamfer(Box, V, Levels, new ChamferParams(ContourSide.Outside, 60, 1, 1.8, 3)).Ok);
    }

    [Fact]
    public void Chanfro_gera_programa_com_aviso_de_ponta_afiada()
    {
        JobBuild build = CamJobBuilder.Build(JobWith(CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0) with { Tool = V }), null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains(build.Warnings, w => w.Contains("ponta afiada"));
        Assert.Contains("SIMULE", build.Program.ToUpperInvariant());
    }

    // ------------------------------------------------ arquivo

    [Fact]
    public void Chanfro_salva_versao_7_e_volta_igual()
    {
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Inside, 0) with { Tool = V, Chamfer = new ChamferParams(ContourSide.Inside, 60, 1.5, 3, 2.5) };
        string json = CamJobJson.ToJson(JobWith(s));
        Assert.Contains("\"version\": 7", json);
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        CamOperationSpec r = back.Job!.Operations[0];
        Assert.Equal(JobOpKind.Chamfer, r.Kind);
        Assert.Equal(new ChamferParams(ContourSide.Inside, 60, 1.5, 3, 2.5), r.Chamfer);
        Assert.Equal(CamJobBuilder.Build(JobWith(s), null).Program, CamJobBuilder.Build(back.Job, null).Program);
    }

    [Fact]
    public void Chanfro_com_campo_vazio_volta_vazio()
    {
        CamOperationSpec s = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0));
        JobLoadResult back = CamJobJson.FromJson(CamJobJson.ToJson(JobWith(s)));
        Assert.True(back.Ok, back.Error);
        Assert.Contains(CamJobInfo.ChamferWidthField, back.Job!.Operations[0].MissingFields);
    }

    [Fact]
    public void Trabalho_sem_chanfro_continua_na_versao_antiga()
    {
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0);
        Assert.DoesNotContain("\"version\": 7", CamJobJson.ToJson(JobWith(s)));
        Assert.DoesNotContain("chamfer", CamJobJson.ToJson(JobWith(s)));
    }

    // ------------------------------------------------ pacote CAM → Code (cone)

    [Fact]
    public void Pacote_leva_o_cone_so_quando_o_T_e_so_de_chanfro()
    {
        CamOperationSpec chamfer = CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0) with { Tool = V, Chamfer = new ChamferParams(ContourSide.Outside, 90, 1, 1, 3) };
        CamJob job = JobWith(chamfer);
        HandoffTool tool = CamHandoff.Create(job, "x", null).Tools.Single();
        Assert.Equal(CamHandoff.VCutterType, tool.Type);
        Assert.Equal(90, tool.TipAngle);
        ToolProfile profile = CamHandoffCode.ProfileOf(tool, 10)!;
        Assert.Equal(ToolTipShape.Cone, profile.Shape);
        Assert.Equal(90, profile.TipAngle);
        Assert.Equal(5, profile.Radius);

        // o mesmo T também num contorno: fica genérico (aviso de simulação não inventa o tipo)
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0) with { Tool = V });
        Assert.Null(CamHandoff.Create(job, "x", null).Tools.Single().Type);
    }

    [Fact]
    public void Pacote_com_angulo_vazio_nao_inventa_cone()
    {
        CamOperationSpec s = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Chamfer, ContourSide.Outside, 0)) with { Tool = V };
        Assert.Null(CamHandoff.Create(JobWith(s), "x", null).Tools.Single().Type);
    }
}
