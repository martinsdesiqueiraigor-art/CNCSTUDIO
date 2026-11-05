using System.Text.Json.Nodes;
using CncStudio.Core.Assist;
using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Marca de origem por operação (missão 11): ida e volta no arquivo, arquivo antigo, marca inválida, edição vira digitado, Assist marca.</summary>
public class CamJobOriginTests
{
    private static readonly OpOrigin DraftMark = new(OpOrigin.Assist, OpOrigin.Draft, ["depth", "heights"]);

    private static CamJob Job(OpOrigin? origin)
    {
        var job = new CamJob { Name = "PLACA", Stock = new JobStock(0, 0, 100, 60, 10) };
        job.Contours.Add(Contour.FromPoints([new(0, 0), new(100, 0), new(100, 60), new(0, 60)]));
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0) with { Origin = origin });
        return job;
    }

    private static string Json(OpOrigin? origin) => CamJobJson.ToJson(Job(origin));

    [Fact]
    public void Salvar_e_abrir_preserva_a_marca_e_a_versao_do_arquivo()
    {
        string json = Json(DraftMark);
        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Empty(loaded.Warnings);
        OpOrigin o = loaded.Job!.Operations[0].Origin!;
        Assert.Equal(OpOrigin.Draft, o.CutData);
        Assert.Equal(["depth", "heights"], o.Typed);
        Assert.Equal(json, CamJobJson.ToJson(loaded.Job));
        Assert.Equal(CamJob.CurrentVersion, (int)JsonNode.Parse(json)!["version"]!);
    }

    [Fact]
    public void Arquivo_antigo_sem_marca_abre_sem_marca_e_sem_aviso()
    {
        string json = Json(null);
        Assert.Null(JsonNode.Parse(json)!["operations"]![0]!["origin"]);
        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok);
        Assert.Null(loaded.Job!.Operations[0].Origin);
        Assert.Empty(loaded.Warnings);
        Assert.Null(OpOrigin.Describe(null));
    }

    [Theory]
    [InlineData("""{"source":"gpt","typed":[]}""")]
    [InlineData("""{"source":"assist","cutData":"talvez","typed":[]}""")]
    [InlineData("""{"source":"assist","typed":["cor"]}""")]
    [InlineData("""{"source":"assist"}""")]
    [InlineData("\"assist\"")]
    public void Marca_invalida_abre_o_trabalho_sem_marca_e_com_aviso(string origin)
    {
        JsonObject root = JsonNode.Parse(Json(null))!.AsObject();
        root["operations"]![0]!["origin"] = JsonNode.Parse(origin);
        JobLoadResult loaded = CamJobJson.FromJson(root.ToJsonString());
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Null(loaded.Job!.Operations[0].Origin);
        Assert.Contains(loaded.Warnings, w => w.Contains("Operação 1") && w.Contains("sem marca"));
    }

    [Fact]
    public void Texto_da_marca_diz_rascunho_em_ambar_e_o_que_foi_digitado()
    {
        (string kind, string text) = OpOrigin.Describe(DraftMark)!.Value;
        Assert.Equal("pending", kind);
        Assert.Equal("do Assist · dados de corte: rascunho · digitado: profundidade, alturas Z", text);
        Assert.Equal("neutral", OpOrigin.Describe(DraftMark with { CutData = OpOrigin.Reviewed })!.Value.Kind);
    }

    [Fact]
    public void Editar_rotacao_ou_avanco_tira_o_rascunho_e_vira_digitado()
    {
        CamOperationSpec before = Job(DraftMark).Operations[0];
        CamOperationSpec rpm = OpOrigin.Track(before, before with { Tool = before.Tool with { SpindleRpm = before.Tool.SpindleRpm + 100 } });
        Assert.Null(rpm.Origin!.CutData);
        Assert.Equal(["depth", "heights", "rpm"], rpm.Origin.Typed);

        CamOperationSpec feed = OpOrigin.Track(before, before with { Tool = before.Tool with { FeedZ = before.Tool.FeedZ + 10 } });
        Assert.Null(feed.Origin!.CutData);
        Assert.Contains("feed", feed.Origin.Typed);
        Assert.DoesNotContain("rascunho", OpOrigin.Describe(feed.Origin)!.Value.Text);
    }

    [Fact]
    public void Editar_fundo_ou_alturas_marca_digitado_e_mantem_o_rascunho()
    {
        CamOperationSpec before = Job(new OpOrigin(OpOrigin.Assist, OpOrigin.Draft, [])).Operations[0];
        CamOperationSpec depth = OpOrigin.Track(before, before with { Levels = before.Levels with { BottomZ = before.Levels.BottomZ - 1 } });
        Assert.Equal(["depth"], depth.Origin!.Typed);
        Assert.Equal(OpOrigin.Draft, depth.Origin.CutData);

        CamOperationSpec heights = OpOrigin.Track(depth, depth with { Levels = depth.Levels with { SafeZ = depth.Levels.SafeZ + 5 } });
        Assert.Equal(["depth", "heights"], heights.Origin!.Typed);
    }

    [Fact]
    public void Operacao_sem_marca_continua_sem_marca_e_editar_o_nome_nao_muda_a_marca()
    {
        CamOperationSpec manual = Job(null).Operations[0];
        Assert.Null(OpOrigin.Track(manual, manual with { Tool = manual.Tool with { SpindleRpm = 1 } }).Origin);

        CamOperationSpec marked = Job(DraftMark).Operations[0];
        Assert.Same(DraftMark, OpOrigin.Track(marked, marked with { Label = "Contorno" }).Origin);
    }

    [Fact]
    public void Aplicar_do_Assist_marca_rascunho_e_profundidade_e_alturas_digitadas()
    {
        var input = new PlanInput([Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(15, 15), 4)], 10,
            AssistLibrary.Defaults().Materials[3], [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8, true, "Broca 8")]);
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Presumed);
        PlanReviewResult r = PlanReview.Review(plan, input, null, depths, ReleaseStrategy.HeldBySetup, new PlanHeights(50, 2, 0));
        Assert.True(r.Ok, r.Error);
        Assert.NotEmpty(r.Operations!);
        Assert.All(r.Operations!, op =>
        {
            Assert.Equal(OpOrigin.Assist, op.Origin!.Source);
            Assert.Equal(input.Material!.Draft ? OpOrigin.Draft : OpOrigin.Reviewed, op.Origin.CutData);
            Assert.Equal(["depth", "heights"], op.Origin.Typed);
        });
        Assert.All(plan.Operations, op => Assert.Null(op.Origin)); // o plano sugerido não muda
    }
}
