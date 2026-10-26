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
