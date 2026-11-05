using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

/// <summary>Giros do modelo 3D (botões de orientação, CAM-08-C2).</summary>
public class ModelTurnTests
{
    // Caixa 10 × 20 × 30 fora da origem, triângulos para fora
    private static List<(Point3, Point3, Point3)> Box()
    {
        const double X0 = 5, Y0 = -3, Z0 = 7, DX = 10, DY = 20, DZ = 30;
        Point3 p000 = new(X0, Y0, Z0), p100 = new(X0 + DX, Y0, Z0), p110 = new(X0 + DX, Y0 + DY, Z0), p010 = new(X0, Y0 + DY, Z0);
        Point3 p001 = new(X0, Y0, Z0 + DZ), p101 = new(X0 + DX, Y0, Z0 + DZ), p111 = new(X0 + DX, Y0 + DY, Z0 + DZ), p011 = new(X0, Y0 + DY, Z0 + DZ);
        return
        [
            (p000, p010, p110), (p000, p110, p100), (p001, p101, p111), (p001, p111, p011),
            (p000, p100, p101), (p000, p101, p001), (p010, p011, p111), (p010, p111, p110),
            (p000, p001, p011), (p000, p011, p010), (p100, p110, p111), (p100, p111, p101),
        ];
    }

    private static void AssertSame(TriangleMesh expected, TriangleMesh actual)
    {
        Assert.Equal(expected.TriangleCount, actual.TriangleCount);
        for (int i = 0; i < expected.TriangleCount; i++)
        {
            Assert.Equal(expected.Triangle(i), actual.Triangle(i));
        }
    }

    [Theory]
    [InlineData(ModelTurn.XPlus90)]
    [InlineData(ModelTurn.XMinus90)]
    [InlineData(ModelTurn.YPlus90)]
    [InlineData(ModelTurn.YMinus90)]
    public void Quatro_giros_de_90_no_mesmo_eixo_voltam_ao_original(ModelTurn turn)
    {
        var mesh = new TriangleMesh(Box());
        TriangleMesh m = mesh;
        for (int i = 0; i < 4; i++)
        {
            m = m.Turned(turn);
        }

        AssertSame(mesh, m);
    }

    [Fact]
    public void Virar_duas_vezes_volta_ao_original_e_giro_mais_e_menos_se_anulam()
    {
        var mesh = new TriangleMesh(Box());
        AssertSame(mesh, mesh.Turned(ModelTurn.Flip).Turned(ModelTurn.Flip));
        AssertSame(mesh, mesh.Turned(ModelTurn.XPlus90).Turned(ModelTurn.XMinus90));
        AssertSame(mesh, mesh.Turned(ModelTurn.YPlus90).Turned(ModelTurn.YMinus90));
    }

    [Theory]
    [InlineData(ModelTurn.XPlus90)]
    [InlineData(ModelTurn.XMinus90)]
    [InlineData(ModelTurn.YPlus90)]
    [InlineData(ModelTurn.YMinus90)]
    [InlineData(ModelTurn.Flip)]
    public void Giro_preserva_volume_area_sentido_e_malha_fechada_ou_aberta(ModelTurn turn)
    {
        var closed = new TriangleMesh(Box());
        TriangleMesh turned = closed.Turned(turn);
        Assert.Equal(closed.Volume, turned.Volume, 9);
        Assert.Equal(closed.SurfaceArea, turned.SurfaceArea, 9);
        Assert.Equal(6000, MeshOrientation.SignedVolume(turned), 9); // continua para fora (não espelhou)
        Assert.True(turned.IsClosed);

        List<(Point3, Point3, Point3)> t = Box();
        t.RemoveRange(0, 2);
        var open = new TriangleMesh(t);
        Assert.False(open.Turned(turn).IsClosed);
        Assert.Equal(MeshCheck.Of(open).BorderEdges, MeshCheck.Of(open.Turned(turn)).BorderEdges);
    }

    [Theory]
    [InlineData(ModelTurn.XPlus90)]
    [InlineData(ModelTurn.XMinus90)]
    [InlineData(ModelTurn.YPlus90)]
    [InlineData(ModelTurn.YMinus90)]
    [InlineData(ModelTurn.Flip)]
    public void Depois_do_giro_e_do_posicionamento_o_ponto_mais_alto_fica_em_Z0(ModelTurn turn)
    {
        TriangleMesh placed = new TriangleMesh(Box()).Turned(turn).PlacedTopAtZero();
        Assert.Equal(0, placed.Bounds.Max.Z, 12);
        Assert.Equal(0, placed.Bounds.Min.X, 12);
        Assert.Equal(0, placed.Bounds.Min.Y, 12);
    }

    [Fact]
    public void Lado_que_vai_para_cima_em_cada_giro()
    {
        // Um ponto em cada direção: confere qual vai para +Z (o lado de cima da usinagem)
        var mesh = new TriangleMesh([(new(0, 1, 0), new(-1, 0, 0), new(1, 0, 0)), (new(0, -1, 0), new(0, 0, 0), new(0, 0, 0))]);
        Assert.Equal(new Point3(0, 0, 1), mesh.Turned(ModelTurn.XPlus90).Triangle(0).A);   // +Y para cima
        Assert.Equal(new Point3(0, 0, 1), mesh.Turned(ModelTurn.XMinus90).Triangle(1).A);  // −Y para cima
        Assert.Equal(new Point3(0, 0, 1), mesh.Turned(ModelTurn.YPlus90).Triangle(0).B);   // −X para cima
        Assert.Equal(new Point3(0, 0, 1), mesh.Turned(ModelTurn.YMinus90).Triangle(0).C);  // +X para cima
    }
}
