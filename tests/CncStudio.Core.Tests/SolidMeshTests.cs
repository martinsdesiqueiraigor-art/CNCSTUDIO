using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

/// <summary>Malha do sólido para a GPU (Fase 1) e câmera em órbita.</summary>
public class SolidMeshTests
{
    private static HeightMap Bloco() => new(0, 0, 40, 20, 1, 0, -10);

    [Fact]
    public void BlocoIntactoTemTopoELadosDentroDoEstoque()
    {
        SolidMesh mesh = SolidMesh.FromHeightMap(Bloco());
        Assert.True(mesh.TriangleCount >= 10);   // topo (2) + 4 lados (8), no mínimo
        Assert.Equal(mesh.TriangleCount * 3, mesh.IndexCount);
        Assert.Equal(mesh.VertexCount * 3, mesh.Positions.Length);
        Assert.Equal(mesh.Positions.Length, mesh.Normals.Length);
        Assert.Equal(mesh.Positions.Length, mesh.Colors.Length);

        ReadOnlySpan<float> p = mesh.Positions;
        for (int i = 0; i < p.Length; i += 3)
        {
            Assert.InRange(p[i], -1e-4f, 40.0001f);
            Assert.InRange(p[i + 1], -1e-4f, 20.0001f);
            Assert.InRange(p[i + 2], -10.0001f, 1e-4f);
        }
    }

    [Fact]
    public void IndicesValidosNormaisUnitariasECoresDe0a1()
    {
        HeightMap map = Bloco();
        map.CutDisc(20, 10, 4, -3);
        SolidMesh mesh = SolidMesh.FromHeightMap(map);

        foreach (int index in mesh.Indices)
        {
            Assert.InRange(index, 0, mesh.VertexCount - 1);
        }

        ReadOnlySpan<float> n = mesh.Normals;
        for (int i = 0; i < n.Length; i += 3)
        {
            Assert.Equal(1, Math.Sqrt(n[i] * n[i] + n[i + 1] * n[i + 1] + n[i + 2] * n[i + 2]), 4);
        }

        foreach (float c in mesh.Colors)
        {
            Assert.InRange(c, 0f, 1f);
        }
    }

    [Fact]
    public void CortarAcrescentaParedesEFundo()
    {
        int intacto = SolidMesh.FromHeightMap(Bloco()).TriangleCount;
        HeightMap map = Bloco();
        map.CutDisc(20, 10, 4, -3);
        SolidMesh cortado = SolidMesh.FromHeightMap(map);
        Assert.True(cortado.TriangleCount > intacto);

        // O fundo do furo aparece na malha na altura do corte
        bool fundo = false;
        ReadOnlySpan<float> p = cortado.Positions;
        for (int i = 2; i < p.Length; i += 3)
        {
            fundo |= Math.Abs(p[i] + 3) < 1e-4;
        }

        Assert.True(fundo);
    }

    [Fact]
    public void OrbitaComElevacao90OlhaDeCimaEAzimute0DeFrente()
    {
        ViewCamera cima = ViewCamera.Orbit(0, 90);
        Assert.True(cima.ToViewer.Z > 0.99);

        ViewCamera frente = ViewCamera.Orbit(0, 0);
        Assert.Equal(-1, frente.ToViewer.Y, 9);
        Assert.Equal(0, ViewCamera.Dot(frente.Right, frente.Up), 9);

        ViewCamera direita = ViewCamera.Orbit(90, 0);
        Assert.Equal(1, direita.ToViewer.X, 9);
    }
}
