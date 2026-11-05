using System.Text;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class DxfImporterTests
{
    private static string Dxf(params string[] entities)
    {
        var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
        foreach (string e in entities)
        {
            sb.Append(e);
        }

        sb.Append("0\nENDSEC\n0\nEOF\n");
        return sb.ToString();
    }

    private static string Line(double x1, double y1, double x2, double y2) =>
        FormattableString.Invariant($"0\nLINE\n8\n0\n10\n{x1}\n20\n{y1}\n30\n0\n11\n{x2}\n21\n{y2}\n31\n0\n");

    [Fact]
    public void QuatroLinhasViramUmContornoFechado()
    {
        DxfImportResult r = DxfImporter.Parse(Dxf(Line(0, 0, 20, 0), Line(20, 0, 20, 10), Line(20, 10, 0, 10), Line(0, 10, 0, 0)));
        Assert.Single(r.Contours);
        Assert.Empty(r.OpenChains);
        Assert.Equal(200, Math.Abs(r.Contours[0].SignedArea), 6);
    }

    [Fact]
    public void LinhasForaDeOrdemEInvertidasTambemFecham()
    {
        DxfImportResult r = DxfImporter.Parse(Dxf(Line(20, 10, 0, 10), Line(0, 0, 20, 0), Line(0, 0, 0, 10), Line(20, 0, 20, 10)));
        Assert.Single(r.Contours);
        Assert.Equal(200, Math.Abs(r.Contours[0].SignedArea), 6);
    }

    [Fact]
    public void CirculoViraContorno()
    {
        DxfImportResult r = DxfImporter.Parse(Dxf("0\nCIRCLE\n10\n5\n20\n5\n40\n10\n"));
        Assert.Single(r.Contours);
        Assert.Equal(Math.PI * 100, r.Contours[0].SignedArea, 6);
    }

    [Fact]
    public void LwPolylineFechadaComBulgeFazArco()
    {
        // "pílula": duas retas e dois semicírculos (bulge 1 = 180°, anti-horário)
        string poly = "0\nLWPOLYLINE\n90\n4\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n42\n1\n10\n20\n20\n10\n10\n0\n20\n10\n42\n1\n";
        DxfImportResult r = DxfImporter.Parse(Dxf(poly));
        Assert.Single(r.Contours);
        Contour c = r.Contours[0];
        Assert.True(c.IsClosed);
        Assert.Equal(4, c.Segments.Count);
        Assert.Equal(2, c.Segments.Count(s => s.IsArc));
        Assert.Equal((20 * 10) + (Math.PI * 25), c.SignedArea, 4);
    }

    [Fact]
    public void BulgeNegativoGiraNoSentidoHorario()
    {
        string poly = "0\nLWPOLYLINE\n90\n2\n70\n1\n10\n0\n20\n0\n42\n-1\n10\n10\n20\n0\n42\n-1\n";
        DxfImportResult r = DxfImporter.Parse(Dxf(poly));
        Assert.Single(r.Contours);
        Assert.False(r.Contours[0].IsCounterClockwise);
        Assert.Equal(Math.PI * 25, Math.Abs(r.Contours[0].SignedArea), 4);
    }

    [Fact]
    public void PolylineAntigaComVertices()
    {
        string v(double x, double y) => FormattableString.Invariant($"0\nVERTEX\n10\n{x}\n20\n{y}\n");
        string poly = "0\nPOLYLINE\n66\n1\n10\n0\n20\n0\n70\n1\n" + v(0, 0) + v(10, 0) + v(10, 10) + v(0, 10) + "0\nSEQEND\n";
        DxfImportResult r = DxfImporter.Parse(Dxf(poly));
        Assert.Single(r.Contours);
        Assert.Equal(100, Math.Abs(r.Contours[0].SignedArea), 6);
    }

    [Fact]
    public void RetasEArcoSoltosFecham()
    {
        // retângulo 20×10 com canto arredondado de raio 3 no canto (20,10)
        string arc = "0\nARC\n10\n17\n20\n7\n40\n3\n50\n0\n51\n90\n";
        DxfImportResult r = DxfImporter.Parse(Dxf(Line(0, 0, 20, 0), Line(20, 0, 20, 7), arc, Line(17, 10, 0, 10), Line(0, 10, 0, 0)));
        Assert.Single(r.Contours);
        Assert.Equal(200 - (9 - (Math.PI * 9 / 4)), Math.Abs(r.Contours[0].SignedArea), 4);
    }

    [Fact]
    public void CadeiaAbertaEntraComoAviso()
    {
        DxfImportResult r = DxfImporter.Parse(Dxf(Line(0, 0, 10, 0), Line(10, 0, 10, 10)));
        Assert.Empty(r.Contours);
        Assert.Single(r.OpenChains);
        Assert.Contains(r.Warnings, w => w.Contains("aberta"));
    }

    [Fact]
    public void TiposNaoSuportadosViramAviso()
    {
        DxfImportResult r = DxfImporter.Parse(Dxf("0\nSPLINE\n10\n0\n20\n0\n", "0\nTEXT\n10\n0\n20\n0\n", Line(0, 0, 1, 0)));
        Assert.Contains(r.Warnings, w => w.Contains("SPLINE"));
        Assert.Contains(r.Warnings, w => w.Contains("TEXT"));
    }

    [Fact]
    public void ArquivoVazioAvisa()
    {
        DxfImportResult r = DxfImporter.Parse("");
        Assert.Empty(r.Contours);
        Assert.Contains(r.Warnings, w => w.Contains("Nenhum contorno"));
    }

    [Fact]
    public void ContornoImportadoPodeSerDeslocado()
    {
        DxfImportResult r = DxfImporter.Parse(Dxf(Line(0, 0, 20, 0), Line(20, 0, 20, 10), Line(20, 10, 0, 10), Line(0, 10, 0, 0)));
        OffsetResult o = r.Contours[0].OffsetOutside(-2);
        Assert.True(o.Ok, o.Error);
        Assert.Equal(96, Math.Abs(o.Contour!.SignedArea), 6);
    }
}

public class DxfUnitsAndPickTests
{
    private static string Header(int? insunits) =>
        "0\nSECTION\n2\nHEADER\n" + (insunits is { } u ? $"9\n$INSUNITS\n70\n{u}\n" : "") + "0\nENDSEC\n";

    private static string SquareDxf(int? insunits, double side) =>
        Header(insunits) + "0\nSECTION\n2\nENTITIES\n" +
        FormattableString.Invariant($"0\nLWPOLYLINE\n8\n0\n90\n4\n70\n1\n10\n0\n20\n0\n10\n{side}\n20\n0\n10\n{side}\n20\n{side}\n10\n0\n20\n{side}\n") +
        "0\nENDSEC\n0\nEOF\n";

    [Theory]
    [InlineData(1, DxfUnits.Inches, 25.4)]
    [InlineData(2, DxfUnits.Feet, 304.8)]
    [InlineData(4, DxfUnits.Millimeters, 1)]
    [InlineData(5, DxfUnits.Centimeters, 10)]
    [InlineData(6, DxfUnits.Meters, 1000)]
    public void Le_a_unidade_do_cabecalho(int code, DxfUnits units, double factor)
    {
        DxfImportResult r = DxfImporter.Parse(SquareDxf(code, 2));
        Assert.Equal(units, r.Units);
        Assert.Equal(factor, r.ToMillimeters, 9);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("unidade"));
    }

    [Fact]
    public void Sem_unidade_avisa_e_assume_milimetros()
    {
        DxfImportResult r = DxfImporter.Parse(SquareDxf(null, 2));
        Assert.Equal(DxfUnits.Unspecified, r.Units);
        Assert.Equal(1, r.ToMillimeters);
        Assert.Contains(r.Warnings, w => w.Contains("não informa a unidade"));
        Assert.Contains(DxfImporter.Parse(SquareDxf(0, 2)).Warnings, w => w.Contains("não informa a unidade"));
    }

    [Fact]
    public void Unidade_estranha_avisa()
    {
        DxfImportResult r = DxfImporter.Parse(SquareDxf(14, 2)); // decímetro
        Assert.Equal(DxfUnits.Other, r.Units);
        Assert.Contains(r.Warnings, w => w.Contains("não é mm"));
    }

    [Fact]
    public void Escala_polegadas_para_milimetros_multiplica_a_area_por_fator_ao_quadrado()
    {
        DxfImportResult r = DxfImporter.Parse(SquareDxf(1, 2)).Scaled(25.4);
        Assert.Equal(50.8 * 50.8, Math.Abs(r.Contours[0].SignedArea), 6);
        (double minX, double minY, double maxX, double maxY) = r.Contours[0].BoundsXY();
        Assert.Equal(0, minX, 6);
        Assert.Equal(0, minY, 6);
        Assert.Equal(50.8, maxX, 6);
        Assert.Equal(50.8, maxY, 6);
    }

    [Fact]
    public void Escala_mantem_arcos_com_raio_e_sentido()
    {
        var circle = Contour.Circle(new Point2(1, 2), 3).Scaled(10);
        Segment2 s = circle.Segments[0];
        Assert.Equal(30, s.Radius, 9);
        Assert.Equal(10, s.Center.X, 9);
        Assert.Equal(20, s.Center.Y, 9);
        Assert.Equal(Math.PI * 900, Math.Abs(circle.SignedArea), 3);
    }

    [Fact]
    public void Clique_escolhe_a_borda_mais_perto_ou_o_menor_contorno_que_contem()
    {
        var outer = Contour.Rectangle(0, 0, 100, 60);
        var inner = Contour.Rectangle(30, 20, 20, 20); // ilha dentro do retângulo
        var far = Contour.Rectangle(200, 0, 10, 10);
        Contour[] all = [outer, inner, far];

        Assert.Equal(0, ContourPick.At(all, new Point2(50, 0.4), 1));     // na borda de baixo do externo
        Assert.Equal(1, ContourPick.At(all, new Point2(30.3, 30), 1));    // na borda da ilha
        Assert.Equal(1, ContourPick.At(all, new Point2(40, 30), 1));      // dentro das duas: vale a menor
        Assert.Equal(0, ContourPick.At(all, new Point2(10, 30), 1));      // só dentro do externo
        Assert.Equal(2, ContourPick.At(all, new Point2(205, 5), 1));
        Assert.Equal(-1, ContourPick.At(all, new Point2(150, 30), 1));    // fora de tudo
        Assert.Equal(-1, ContourPick.At([], new Point2(0, 0), 1));
    }

    [Fact]
    public void Circulo_e_escolhido_pelo_interior()
    {
        Contour[] all = [Contour.Circle(new Point2(0, 0), 10), Contour.Circle(new Point2(50, 0), 5)];
        Assert.Equal(0, ContourPick.At(all, new Point2(2, 3), 0.5));
        Assert.Equal(1, ContourPick.At(all, new Point2(50, 0), 0.5));
        Assert.Equal(-1, ContourPick.At(all, new Point2(25, 0), 0.5));
    }
}

public class DxfParseInMillimetersTests
{
    [Fact]
    public void Polegadas_viram_milimetros_e_avisam()
    {
        string dxf = "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n8\n0\n10\n1\n20\n1\n40\n0.5\n0\nENDSEC\n0\nEOF\n";
        DxfImportResult r = DxfImporter.ParseInMillimeters(dxf);
        Segment2 s = r.Contours[0].Segments[0];
        Assert.Equal(12.7, s.Radius, 9);
        Assert.Equal(25.4, s.Center.X, 9);
        Assert.Contains(r.Warnings, w => w.Contains("polegadas") && w.Contains("25,4"));
    }

    [Fact]
    public void Milimetros_ficam_como_estao()
    {
        string dxf = "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n8\n0\n10\n1\n20\n1\n40\n5\n0\nENDSEC\n0\nEOF\n";
        DxfImportResult r = DxfImporter.ParseInMillimeters(dxf);
        Assert.Equal(5, r.Contours[0].Segments[0].Radius, 9);
        Assert.Empty(r.Warnings);
    }
}
