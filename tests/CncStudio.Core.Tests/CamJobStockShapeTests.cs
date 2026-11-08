using CncStudio.Core.Cam;
using CncStudio.Core.Scene;

namespace CncStudio.Core.Tests;

/// <summary>UX-04f: bruto da fresa em bloco por sobra, cilindro e tubo; altura da peça; arquivo versão 1/2; 3D. G-code nunca muda.</summary>
public class CamJobStockShapeTests
{
    private static readonly CamTool Tool = new(1, 10, 3000, 500, 200, "Fresa D10");
    private static readonly CamLevels Levels = new(50, 2, 0, -6, 3);

    /// <summary>Peça 60 × 40 com canto em (0, 0) e um contorno por fora.</summary>
    private static CamJob Job()
    {
        var job = new CamJob { Name = "BRUTO NOVO" };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, Tool, Levels) { ContourIndex = 0, ContourParams = new ContourParams(ContourSide.Outside) });
        return job;
    }

    private static JobStockShape Margins() => new(StockShapeKind.Margins) { XMinus = 2, XPlus = 3, YMinus = 4, YPlus = 5, ZTop = 1, ZBottom = 6 };

    private static JobStockShape Tube() => new(StockShapeKind.Tube) { Axis = StockAxis.X, Diameter = 80, InnerDiameter = 30, Length = 120 };

    [Fact]
    public void Bloco_por_sobra_parte_da_peca_com_Z0_no_topo_dela()
    {
        CamJob job = Job();
        job.PartHeight = 20;
        job.SetStock(null, Margins() with { OffsetX = 1 });
        StockSolid s = CamJobStock.Check(job).Solid!;
        Assert.Equal(StockSolidKind.Box, s.Kind);
        Assert.Equal((-1.0, 64.0), (s.MinX, s.MaxX));   // −2 + 1 .. 60 + 3 + 1
        Assert.Equal((-4.0, 45.0), (s.MinY, s.MaxY));
        Assert.Equal((-26.0, 1.0), (s.MinZ, s.MaxZ));   // peça de −20 a 0; sobra 6 embaixo e 1 em cima
        Assert.Equal("Bloco por sobra 65 × 49 × 27 mm", CamJobStock.Title(job));
        Assert.Equal(("ok", "OK"), CamJobStock.State(job));
        Assert.Equal(new JobStock(-1, -4, 65, 49, 27), CamJobStock.BlockBox(job));
    }

    [Fact]
    public void Cilindro_e_tubo_ficam_centrados_na_peca()
    {
        CamJob job = Job();
        job.PartHeight = 20;
        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder) { Diameter = 80, Length = 30 });
        StockSolid c = CamJobStock.Check(job).Solid!;
        Assert.Equal((-10.0, 70.0, -20.0, 60.0, -25.0, 5.0), (c.MinX, c.MaxX, c.MinY, c.MaxY, c.MinZ, c.MaxZ));
        Assert.Equal("Cilindro Ø80 × 30 mm", CamJobStock.Title(job));

        job.SetStock(null, Tube());
        StockSolid t = CamJobStock.Check(job).Solid!;
        Assert.Equal(StockSolidKind.Tube, t.Kind);
        Assert.Equal((-30.0, 90.0), (t.MinX, t.MaxX));   // comprimento ao longo de X, centro em X 30
        Assert.Equal((-50.0, 30.0), (t.MinZ, t.MaxZ));   // centro em Z −10 (meio da peça)
        Assert.Equal("Tubo Ø80/Ø30 × 120 mm (eixo X)", CamJobStock.Title(job));
        Assert.Null(CamJobStock.BlockBox(job));
    }

    [Fact]
    public void Tubo_com_furo_maior_ou_igual_ao_externo_e_invalido_com_o_motivo()
    {
        CamJob job = Job();
        job.PartHeight = 20;
        job.SetStock(null, Tube() with { InnerDiameter = 85 });
        StockCheck c = CamJobStock.Check(job);
        Assert.False(c.Complete);
        Assert.Contains("Ø interno precisa ser menor que o Ø externo.", c.Invalid);
        job.SetStock(null, Tube() with { InnerDiameter = 80 });
        Assert.Contains("Ø interno precisa ser menor que o Ø externo.", CamJobStock.Check(job).Invalid);
        Assert.Contains("sobra não pode ser negativa", Margins().With(JobStockShape.XPlusField, -1).InvalidReason(JobStockShape.XPlusField));
    }

    [Fact]
    public void Campo_vazio_deixa_o_bruto_incompleto_sem_travar_gerar()
    {
        CamJob job = Job();
        job.PartHeight = 20;
        job.SetStock(null, new JobStockShape(StockShapeKind.Tube) { Axis = StockAxis.Z, Diameter = 80 });
        StockCheck c = CamJobStock.Check(job);
        Assert.Null(c.Solid);
        Assert.Equal(["Ø interno", "Comprimento"], c.Missing);
        Assert.Equal(("pending", "incompleto: falta Ø interno, Comprimento"), CamJobStock.State(job));

        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok);
        string pending = Assert.Single(CamJobInfo.Pending(job, build), p => p.StartsWith("Bruto", StringComparison.Ordinal));
        Assert.Equal("Bruto incompleto: falta Ø interno, Comprimento", pending);
        Assert.False(CamJobInfo.Blocks(pending));
        Assert.Empty(CamJobInfo.SaveBlockers(job));
    }

    [Fact]
    public void Sem_bruto_nenhum_continua_travando_como_antes()
    {
        CamJob job = Job();
        IReadOnlyList<string> pending = CamJobInfo.Pending(job, CamJobBuilder.Build(job, null));
        Assert.Contains(CamJobInfo.StockMissing, pending);
        Assert.True(CamJobInfo.Blocks(CamJobInfo.StockMissing));
        Assert.Equal("Bruto: não definido", CamJobStock.Title(job));
    }

    [Theory]
    [InlineData(StockShapeKind.Margins)]
    [InlineData(StockShapeKind.Cylinder)]
    [InlineData(StockShapeKind.Tube)]
    public void Formato_novo_sem_altura_da_peca_fica_incompleto(StockShapeKind kind)
    {
        CamJob job = Job();
        job.SetStock(null, kind switch
        {
            StockShapeKind.Margins => Margins(),
            StockShapeKind.Cylinder => new JobStockShape(kind) { Diameter = 80, Length = 30 },
            _ => Tube(),
        });
        Assert.Equal([CamJobStock.PartHeightField], CamJobStock.Check(job).Missing);
        job.PartHeight = 0;
        Assert.Contains("A altura Z da peça precisa ser maior que zero.", CamJobStock.Check(job).Invalid);
    }

    [Fact]
    public void Bloco_absoluto_nao_precisa_da_altura_da_peca()
    {
        CamJob job = Job();
        job.Stock = new JobStock(-5, -5, 70, 50, 20);
        StockSolid s = CamJobStock.Check(job).Solid!;
        Assert.Equal((-20.0, 0.0), (s.MinZ, s.MaxZ));
        Assert.Equal("Bloco absoluto 70 × 50 × 20 mm", CamJobStock.Title(job));
        Assert.Equal(job.Stock, CamJobStock.BlockBox(job));
    }

    [Fact]
    public void Trocar_o_desenho_recalcula_o_bloco_com_as_mesmas_sobras_e_avisa()
    {
        CamJob job = Job();
        job.PartHeight = 20;
        job.SetStock(null, Margins());
        StockSolid before = CamJobStock.Check(job).Solid!;
        Assert.Null(CamJobStock.RecalculatedNotice(job, before));

        job.Contours.Clear();
        job.Contours.Add(Contour.Rectangle(0, 0, 100, 80));
        StockSolid after = CamJobStock.Check(job).Solid!;
        Assert.Equal((105.0, 89.0), (after.SizeX, after.SizeY));   // mesmas sobras: 2 + 3 e 4 + 5
        Assert.Equal("Bruto recalculado com as mesmas sobras: 105 × 89 × 27 mm.", CamJobStock.RecalculatedNotice(job, before));

        job.SetStock(null, Tube());
        Assert.Null(CamJobStock.RecalculatedNotice(job, before));
    }

    [Fact]
    public void Arquivo_sem_nada_novo_continua_na_versao_1()
    {
        CamJob job = Job();
        job.Stock = new JobStock(-5, -5, 70, 50, 20);
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 1", json);
        Assert.DoesNotContain("stockShape", json);
        Assert.DoesNotContain("partHeight", json);
    }

    [Fact]
    public void Formato_novo_grava_versao_2_so_com_os_campos_preenchidos()
    {
        CamJob job = Job();
        job.SetStock(null, new JobStockShape(StockShapeKind.Tube) { Axis = StockAxis.Y, Diameter = 80, Length = 120 });
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"kind\": \"Tube\"", json);
        Assert.Contains("\"axis\": \"Y\"", json);
        Assert.DoesNotContain("innerDiameter", json);   // vazio não vai para o arquivo
        Assert.DoesNotContain("partHeight", json);
        Assert.DoesNotContain("\"stock\":", json);

        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Equal(job.StockShape, loaded.Job!.StockShape);
        Assert.Null(loaded.Job.StockShape!.InnerDiameter);
        Assert.Equal(json, CamJobJson.ToJson(loaded.Job));

        var heightOnly = Job();
        heightOnly.PartHeight = 12.5;
        string h = CamJobJson.ToJson(heightOnly);
        Assert.Contains("\"version\": 2", h);
        Assert.Equal(12.5, CamJobJson.FromJson(h).Job!.PartHeight);
    }

    [Fact]
    public void Leitor_recusa_versao_maior_que_a_atual()
    {
        string json = CamJobJson.ToJson(Job()).Replace("\"version\": 1", "\"version\": 3");
        JobLoadResult r = CamJobJson.FromJson(json);
        Assert.False(r.Ok);
        Assert.Contains("versão mais nova (3)", r.Error);
    }

    [Fact]
    public void Bruto_novo_estragado_abre_como_nao_definido_com_aviso()
    {
        string json = CamJobJson.ToJson(Job()).Replace("\"version\": 1,", "\"version\": 2, \"stockShape\": { \"kind\": \"Esfera\" },");
        JobLoadResult r = CamJobJson.FromJson(json);
        Assert.True(r.Ok, r.Error);
        Assert.Null(r.Job!.StockShape);
        Assert.Contains(r.Warnings, w => w.Contains("Bruto do arquivo ignorado"));
    }

    [Fact]
    public void Bruto_vazio_ou_incompleto_gera_o_mesmo_programa_e_salva()
    {
        CamJob plain = Job();
        string program = CamJobBuilder.Build(plain, null).Program;

        CamJob[] variants =
        [
            WithShape(new JobStockShape(StockShapeKind.Margins), null),
            WithShape(Tube() with { InnerDiameter = 99 }, 20),
            WithShape(Margins(), 20),
            WithShape(Tube(), 20),
        ];
        foreach (CamJob v in variants)
        {
            Assert.Equal(program, CamJobBuilder.Build(v, null).Program);
            JobLoadResult again = CamJobJson.FromJson(CamJobJson.ToJson(v));
            Assert.True(again.Ok, again.Error);
            Assert.Equal(program, CamJobBuilder.Build(again.Job!, null).Program);
        }

        static CamJob WithShape(JobStockShape shape, double? height)
        {
            CamJob j = Job();
            j.PartHeight = height;
            j.SetStock(null, shape);
            return j;
        }
    }

    [Fact]
    public void Arquivo_antigo_abre_como_bloco_absoluto()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia", "fresa-bloco-siemens.casillas-job"));
        CamJob job = CamJobJson.FromJson(json).Job!;
        Assert.Null(job.StockShape);
        Assert.Null(job.PartHeight);
        Assert.Equal(new JobStock(-5, -5, 70, 50, 20), job.Stock);
        Assert.Equal("Bloco absoluto", CamJobStock.KindName(job));
        Assert.True(CamJobStock.Check(job).Complete);
    }

    [Fact]
    public void Trocar_o_formato_deixa_uma_fonte_so()
    {
        CamJob job = Job();
        job.Stock = new JobStock(0, 0, 10, 10, 10);
        job.SetStock(job.Stock, Margins());
        Assert.Null(job.Stock);
        job.SetStock(new JobStock(0, 0, 10, 10, 10), null);
        Assert.Null(job.StockShape);
        Assert.NotNull(job.Stock);
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData(" 3.25 ", 3.25)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void Numero_digitado_vazio_ou_texto_vira_vazio(string typed, double? expected) =>
        Assert.Equal(expected, CamJobStock.ParseTyped(typed));

    // ---------------------------------------------------------------- 3D

    [Fact]
    public void Cena_desenha_cada_formato_no_lugar_do_bruto()
    {
        CamJob job = Job();
        job.PartHeight = 20;
        foreach (JobStockShape shape in new[] { Margins(), new JobStockShape(StockShapeKind.Cylinder) { Diameter = 80, Length = 30 }, Tube(), Tube() with { Axis = StockAxis.Y } })
        {
            job.SetStock(null, shape);
            StockSolid s = CamJobStock.Check(job).Solid!;
            SceneSnapshot scene = CamJobScene.BuildResolved(s, job.Contours, null, CamViewOptions.Default);
            SceneObjectState stock = Assert.Single(scene.Objects, o => o.Id == CamJobScene.StockId);
            float[] p = stock.Mesh.Positions.ToArray();
            (double x0, double x1) = Range(p, 0);
            (double y0, double y1) = Range(p, 1);
            (double z0, double z1) = Range(p, 2);
            Assert.Equal(s.MinX, x0, 3);
            Assert.Equal(s.MaxX, x1, 3);
            Assert.Equal(s.MinY, y0, 3);
            Assert.Equal(s.MaxY, y1, 3);
            Assert.Equal(s.MinZ, z0, 3);
            Assert.Equal(s.MaxZ, z1, 3);
        }

        // incompleto: sem bruto na cena
        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder));
        Assert.DoesNotContain(CamJobScene.BuildResolved(CamJobStock.Check(job).Solid, job.Contours, null, CamViewOptions.Default).Objects, o => o.Id == CamJobScene.StockId);
    }

    [Fact]
    public void Tubo_tem_o_furo_e_as_normais_para_fora()
    {
        StockSolid t = StockSolid.Round(true, StockAxis.X, 0, 0, 0, 80, 30, 100);
        var mesh = CamJobScene.StockMesh(t, 0xFFFFFF);
        float[] p = mesh.Positions.ToArray(), n = mesh.Normals.ToArray();
        double minRadius = double.MaxValue;
        for (int i = 0; i < p.Length; i += 3)
        {
            double r = Math.Sqrt(p[i + 1] * p[i + 1] + p[i + 2] * p[i + 2]);
            minRadius = Math.Min(minRadius, r);
            // parede de fora (r = 40): a normal aponta para longe do eixo X
            if (Math.Abs(r - 40) < 1e-3 && Math.Abs(p[i]) < 49)
            {
                Assert.True(p[i + 1] * n[i + 1] + p[i + 2] * n[i + 2] > 0);
            }
        }

        Assert.Equal(15, minRadius, 3);   // o furo: nada dentro de Ø30
    }

    [Fact]
    public void Bloco_absoluto_desenha_igual_ao_de_antes()
    {
        var stock = new JobStock(-5, -5, 70, 50, 20);
        SceneSnapshot a = CamJobScene.Build(stock, [], null, CamViewOptions.Default);
        SceneSnapshot b = CamJobScene.BuildResolved(StockSolid.Box(-5, -5, -20, 70, 50, 20), [], null, CamViewOptions.Default);
        Assert.Equal(a.Objects[0].Mesh.Positions.ToArray(), b.Objects[0].Mesh.Positions.ToArray());
        Assert.Equal(a.Bounds, b.Bounds);
    }

    private static (double Min, double Max) Range(float[] p, int axis)
    {
        double min = double.MaxValue, max = double.MinValue;
        for (int i = axis; i < p.Length; i += 3)
        {
            min = Math.Min(min, p[i]);
            max = Math.Max(max, p[i]);
        }

        return (min, max);
    }
}
