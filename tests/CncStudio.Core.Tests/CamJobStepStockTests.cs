using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Scene;

namespace CncStudio.Core.Tests;

/// <summary>UX-04f PR 2: bruto em STEP (fundido ou forjado). O arquivo guarda caminho e resumo; abrir nunca lê o STEP; G-code igual.</summary>
public class CamJobStepStockTests
{
    private static readonly CamTool Tool = new(1, 10, 3000, 500, 200, "Fresa D10");
    private static readonly CamLevels Levels = new(50, 2, 0, -6, 3);

    private static CamJob Job()
    {
        var job = new CamJob { Name = "BRUTO STEP" };
        job.Contours.Add(Contour.Rectangle(0, 0, 30, 25));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, Tool, Levels) { ContourIndex = 0, ContourParams = new ContourParams(ContourSide.Outside) });
        return job;
    }

    /// <summary>Caixa fechada com os triângulos para fora (canto mínimo e medidas), como a malha que o leitor de STEP entrega.</summary>
    internal static TriangleMesh BoxMesh(double x0, double y0, double z0, double sx, double sy, double sz)
    {
        double x1 = x0 + sx, y1 = y0 + sy, z1 = z0 + sz;
        Point3 P(double x, double y, double z) => new(x, y, z);
        var t = new List<(Point3, Point3, Point3)>();
        void Quad(Point3 a, Point3 b, Point3 c, Point3 d)
        {
            t.Add((a, b, c));
            t.Add((a, c, d));
        }

        Quad(P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0)); // fundo (−Z)
        Quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)); // topo (+Z)
        Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)); // −Y
        Quad(P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0)); // +Y
        Quad(P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), P(x0, y1, z0)); // −X
        Quad(P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1)); // +X
        return new TriangleMesh(t);
    }

    /// <summary>O bloco de referência Bloco_32x29x22.step (volume 20,42 cm³), com o canto onde o STEP o deixar.</summary>
    private static TriangleMesh Block() => BoxMesh(-1, -2, -22, 32, 29, 22);

    private const string StepPath = @"C:\brutos\Bloco_32x29x22.step";

    private static CamJob StepJob()
    {
        CamJob job = Job();
        job.SetStock(null, CamJobStock.ImportStep(null, StepPath, Block()));
        return job;
    }

    [Fact]
    public void Resumo_tem_a_caixa_e_o_volume_do_bloco()
    {
        StepStockSummary s = StepStockSummary.From(Block());
        Assert.Equal((32.0, 29.0, 22.0), (s.SizeX, s.SizeY, s.SizeZ));
        Assert.Equal(32 * 29 * 22, s.Volume, 6);
        Assert.Equal("20,42 cm³", CamJobStock.VolumeText(s.Volume));
    }

    [Fact]
    public void Como_esta_no_STEP_vale_a_caixa_do_arquivo_sem_depender_do_desenho()
    {
        CamJob job = StepJob();
        StockSolid s = CamJobStock.Check(job).Solid!;
        Assert.Equal(StockSolidKind.Step, s.Kind);
        Assert.Equal((-1.0, 31.0, -2.0, 27.0, -22.0, 0.0), (s.MinX, s.MaxX, s.MinY, s.MaxY, s.MinZ, s.MaxZ));
        Assert.Null(job.PartHeight);                       // o STEP não pede a altura da peça
        Assert.Equal("STEP Bloco_32x29x22.step 32 × 29 × 22 mm, 20,42 cm³", CamJobStock.Title(job));
        Assert.Equal(("ok", "OK"), CamJobStock.State(job));

        // trocar o desenho não mexe no bruto STEP (nada é alinhado sozinho)
        job.Contours.Clear();
        job.Contours.Add(Contour.Rectangle(100, 100, 5, 5));
        Assert.Equal(s, CamJobStock.Check(job).Solid);
    }

    [Theory]
    [InlineData(@"C:\brutos\Bloco_32x29x22.step")]          // trabalho salvo no Windows
    [InlineData("/home/igor/brutos/Bloco_32x29x22.step")]   // trabalho salvo no Linux
    [InlineData(@"D:\pasta/mista\Bloco_32x29x22.step")]
    public void Nome_do_STEP_sai_sem_a_pasta_com_qualquer_barra(string path)
    {
        CamJob job = StepJob();
        job.StockShape = job.StockShape! with { StepPath = path };
        Assert.Equal("Bloco_32x29x22.step", CamJobStock.StepFileName(job));
        Assert.Equal("STEP Bloco_32x29x22.step 32 × 29 × 22 mm, 20,42 cm³", CamJobStock.Title(job));
    }

    [Fact]
    public void Digitar_XYZ_soma_o_deslocamento_e_vazio_deixa_incompleto()
    {
        CamJob job = StepJob();
        job.StockShape = job.StockShape! with { Placement = StepPlacement.Typed, OffsetX = 10, OffsetY = 0, OffsetZ = -3 };
        StockSolid s = CamJobStock.Check(job).Solid!;
        Assert.Equal((9.0, 41.0, -25.0, -3.0), (s.MinX, s.MaxX, s.MinZ, s.MaxZ));

        TriangleMesh placed = CamJobStock.PlaceStepMesh(job.StockShape, Block());
        Assert.Equal(new Point3(9, -2, -25), placed.Bounds.Min);

        job.StockShape = job.StockShape with { OffsetY = null };
        Assert.Equal(("pending", "incompleto: falta Deslocamento Y"), CamJobStock.State(job));
    }

    [Fact]
    public void Sem_arquivo_o_STEP_fica_incompleto()
    {
        CamJob job = Job();
        job.SetStock(null, new JobStockShape(StockShapeKind.Step));
        Assert.Equal(("pending", "incompleto: falta o arquivo STEP"), CamJobStock.State(job));
    }

    [Fact]
    public void Arquivo_versao_2_grava_so_caminho_posicao_e_resumo_e_reabre_igual()
    {
        CamJob job = StepJob();
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"kind\": \"Step\"", json);
        Assert.Contains("\"placement\": \"AsInFile\"", json);
        Assert.Contains("\"volume\"", json);
        Assert.DoesNotContain("offsetX", json);   // como está no STEP: o deslocamento não vale e não vai
        Assert.DoesNotContain("axis", json);
        Assert.DoesNotContain("partHeight", json);

        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Equal(job.StockShape!.StepPath, loaded.Job!.StockShape!.StepPath);
        Assert.Equal(job.StockShape.StepSummary, loaded.Job.StockShape.StepSummary);
        Assert.Equal(CamJobStock.Check(job).Solid, CamJobStock.Check(loaded.Job).Solid);
        Assert.Equal(json, CamJobJson.ToJson(loaded.Job));

        job.StockShape = job.StockShape with { Placement = StepPlacement.Typed, OffsetX = 5, OffsetY = null, OffsetZ = 0 };
        string typed = CamJobJson.ToJson(job);
        Assert.Contains("\"offsetX\": 5", typed);
        Assert.DoesNotContain("offsetY", typed);   // vazio não vai para o arquivo
        Assert.Equal(job.StockShape.OffsetZ, CamJobJson.FromJson(typed).Job!.StockShape!.OffsetZ);
    }

    [Fact]
    public void Abrir_o_trabalho_nao_le_o_STEP()
    {
        // o caminho não existe nesta máquina: abrir funciona só com o resumo do arquivo
        string json = CamJobJson.ToJson(StepJob()).Replace(@"C:\\brutos\\", @"Z:\\nao-existe\\");
        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.NotNull(CamJobStock.Check(loaded.Job!).Solid);
        Assert.Null(CamJobStock.Check(loaded.Job!).Solid!.Mesh);   // nenhuma malha lida

        // a conferência ao abrir só pergunta se o arquivo existe (nunca abre o arquivo)
        var asked = new List<string>();
        CamJobStock.VerifyStepFile(loaded.Job!, p =>
        {
            asked.Add(p);
            return true;
        });
        Assert.Equal([@"Z:\nao-existe\Bloco_32x29x22.step"], asked);
    }

    [Fact]
    public void Arquivo_que_sumiu_deixa_incompleto_trava_gerar_e_enviar_e_nao_salvar()
    {
        CamJob job = StepJob();
        Assert.True(CamJobStock.VerifyStepFile(job, _ => false));
        StockCheck c = CamJobStock.Check(job);
        Assert.Null(c.Solid);   // nada suposto: nem a caixa do resumo vale
        Assert.Contains("não encontrado", CamJobStock.IncompleteReason(c));
        Assert.Contains("Localizar de novo", CamJobStock.IncompleteReason(c));

        JobBuild build = CamJobBuilder.Build(job, null);
        string pending = Assert.Single(CamJobInfo.Pending(job, build), p => p.StartsWith("Bruto", StringComparison.Ordinal));
        Assert.StartsWith(CamJobStock.IncompletePrefix, pending);
        Assert.True(CamJobInfo.Blocks(pending));
        Assert.Empty(CamJobInfo.SaveBlockers(job));

        // o motivo é só da tela: o arquivo salvo continua igual
        Assert.DoesNotContain("encontrado", CamJobJson.ToJson(job));

        // localizar de novo: caminho e resumo novos, o motivo some
        job.StockShape = CamJobStock.ImportStep(job.StockShape, @"D:\novo\Bloco.step", Block());
        Assert.True(CamJobStock.Check(job).Complete);
        Assert.False(CamJobStock.VerifyStepFile(job, _ => true));
    }

    [Fact]
    public void Gcode_igual_com_bruto_STEP_completo_incompleto_ou_sem_bruto()
    {
        CamJob job = StepJob();
        string withStep = CamJobBuilder.Build(job, null).Program;
        CamJobStock.VerifyStepFile(job, _ => false);
        string missing = CamJobBuilder.Build(job, null).Program;
        job.SetStock(null, null);
        string none = CamJobBuilder.Build(job, null).Program;
        Assert.Equal(none, withStep);
        Assert.Equal(none, missing);
    }

    [Fact]
    public void Assist_nao_recebe_espessura_do_bruto_STEP()
    {
        CamJob job = StepJob();
        Assert.True(CamJobStock.Check(job).Complete);
        Assert.Null(CamJobStock.BlockBox(job));
    }

    [Fact]
    public void Resumo_estragado_no_arquivo_abre_como_nao_definido_com_aviso()
    {
        string json = CamJobJson.ToJson(StepJob()).Replace("\"volume\":", "\"volume\": \"muito\", \"x\":");
        JobLoadResult r = CamJobJson.FromJson(json);
        Assert.True(r.Ok, r.Error);
        Assert.Null(r.Job!.StockShape);
        Assert.Contains(r.Warnings, w => w.Contains("Bruto do arquivo ignorado"));
    }

    [Fact]
    public void Cena_desenha_a_caixa_do_resumo_e_depois_o_solido_lido()
    {
        CamJob job = StepJob();
        StockSolid box = CamJobStock.Check(job).Solid!;
        SolidMesh onlyBox = CamJobScene.StockMesh(box, 0xFFFFFF);
        Assert.Equal(new Point3(-1, -2, -22), onlyBox.Bounds.Min);

        StockSolid withMesh = box with { Mesh = CamJobStock.PlaceStepMesh(job.StockShape!, BoxMesh(-1, -2, -22, 32, 29, 22)) };
        SolidMesh solid = CamJobScene.StockMesh(withMesh, 0xFFFFFF);
        Assert.Equal(new Point3(31, 27, 0), solid.Bounds.Max);
    }
}
