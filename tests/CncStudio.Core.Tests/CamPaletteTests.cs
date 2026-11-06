using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class CamPaletteTests
{
    private static CamPaletteItem Item(string id) => CamPalette.Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void Faixa_tem_as_mesmas_operacoes_da_lista_antiga_uma_vez_cada()
    {
        var visible = CamPalette.VisibleGroups.SelectMany(g => g.Items).ToList();
        Assert.All(visible, i => Assert.NotNull(i.Kind));
        Assert.Equal(CamJobInfo.NewKinds.Count, visible.Count);
        foreach ((string _, JobOpKind kind, ContourSide side) in CamJobInfo.NewKinds)
        {
            Assert.Single(visible, i => i.Kind == kind && (kind != JobOpKind.Contour || i.Side == side));
        }
    }

    [Fact]
    public void Grupos_na_ordem_e_3D_definido_mas_oculto()
    {
        Assert.Equal(["2D", "Furação", "4º eixo"], CamPalette.VisibleGroups.Select(g => g.Title));
        CamPaletteGroup g3d = CamPalette.Groups.Single(g => g.Id == "3d");
        Assert.False(g3d.Visible);
        Assert.All(g3d.Items, i => Assert.Null(i.Kind));
        Assert.Null(CamPalette.NewOperation(g3d.Items[0], 0));
        Assert.NotNull(CamPalette.Unavailable(g3d.Items[0], new CamJob(), 0));
    }

    [Fact]
    public void Ids_unicos()
    {
        var ids = CamPalette.Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Sem_desenho_so_texto_no_cilindro_serve()
    {
        var job = new CamJob();
        int target = CamPalette.TargetContour(job, null, null);
        Assert.Equal(-1, target);
        var ok = CamPalette.VisibleGroups.SelectMany(g => g.Items).Where(i => CamPalette.Unavailable(i, job, target) is null).Select(i => i.Id);
        Assert.Equal(["texto-cilindro"], ok);
        Assert.Contains("círculo", CamPalette.Unavailable(Item("furacao"), job, target));
    }

    [Fact]
    public void Contorno_aberto_apaga_contorno_e_bolsao_mas_nao_faceamento()
    {
        var job = new CamJob();
        job.Contours.Add(new Contour([Segment2.Line(new Point2(0, 0), new Point2(50, 0)), Segment2.Line(new Point2(50, 0), new Point2(50, 30))]));
        Assert.False(job.Contours[0].IsClosed);
        Assert.Contains("Contorno 1 aberto", CamPalette.Unavailable(Item("bolsao"), job, 0));
        Assert.Contains("Feche a cadeia no DXF", CamPalette.Unavailable(Item("contorno-linha"), job, 0));
        Assert.Null(CamPalette.Unavailable(Item("faceamento"), job, 0));
    }

    [Fact]
    public void Alvo_e_o_contorno_selecionado_senao_o_da_operacao_senao_o_primeiro()
    {
        var job = new CamJob();
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Contours.Add(Contour.Circle(new Point2(30, 20), 5));
        Assert.Equal(1, CamPalette.TargetContour(job, 1, 0));
        Assert.Equal(1, CamPalette.TargetContour(job, null, 1));
        Assert.Equal(0, CamPalette.TargetContour(job, null, -1));
        Assert.Equal(0, CamPalette.TargetContour(job, 7, null));
        Assert.Null(CamPalette.Unavailable(Item("furacao"), job, 0));
    }

    [Fact]
    public void Botao_cria_a_mesma_operacao_da_lista_antiga()
    {
        CamOperationSpec? s = CamPalette.NewOperation(Item("contorno-dentro"), 1);
        Assert.Equal(CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Inside, 1), s);
        Assert.Equal(-1, CamPalette.NewOperation(Item("furacao"), 1)!.ContourIndex);
        Assert.Equal("contorno-dentro", CamPalette.ItemFor(s!)!.Id);
        Assert.Equal("bolsao", CamPalette.ItemFor(CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0))!.Id);
    }

    [Fact]
    public void Dica_diferencia_sem_desenho_de_dxf_so_com_contorno_aberto()
    {
        // DXF só com linha aberta: a cadeia fica fora da Peça (0 contornos), mas a dica não pode mandar "abrir um DXF"
        var job = new CamJob();
        DxfImportResult r = DxfImporter.ParseInMillimeters(string.Join('\n', "0", "SECTION", "2", "ENTITIES", "0", "LWPOLYLINE", "8", "0", "90", "4", "70", "0", "10", "0", "20", "0", "10", "50", "20", "0", "10", "50", "20", "50", "10", "0", "20", "50", "0", "ENDSEC", "0", "EOF", ""));
        Assert.Empty(r.Contours);
        Assert.Single(r.OpenChains);
        job.Contours.AddRange(r.Contours);
        int target = CamPalette.TargetContour(job, null, null);
        foreach (string id in new[] { "faceamento", "contorno-fora", "contorno-dentro", "contorno-linha", "bolsao" })
        {
            string why = CamPalette.Unavailable(Item(id), job, target, r.OpenChains.Count)!;
            Assert.StartsWith("Contorno aberto", why);
            Assert.DoesNotContain("Abra um DXF", why);
        }

        Assert.StartsWith("Abra um DXF", CamPalette.Unavailable(Item("bolsao"), job, target));
        Assert.Contains("círculo", CamPalette.Unavailable(Item("furacao"), job, target, r.OpenChains.Count));
        Assert.Null(CamPalette.Unavailable(Item("texto-cilindro"), job, target, r.OpenChains.Count));
    }
}
