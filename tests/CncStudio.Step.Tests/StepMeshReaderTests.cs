using System.Text;
using System.Text.RegularExpressions;
using CncStudio.Core.Cam3D;
using CncStudio.Step;
using OcctNet.Wrapper;

namespace CncStudio.Step.Tests;

public class StepMeshReaderTests
{
    private const double PlateVolume = (100 * 60 * 20) - (Math.PI * 10 * 10 * 20);

    /// <summary>STEP de uma placa 100×60×20 com um furo passante Ø20 (gerado pelo próprio OpenCASCADE).</summary>
    private static byte[] PlateStep()
    {
        string path = Path.Combine(Path.GetTempPath(), "placa-" + Guid.NewGuid().ToString("N") + ".step");
        try
        {
            using var box = new OcctBox(100, 60, 20);
            using var hole = new OcctCylinder(10, 40);
            using var moved = hole.Translate(new OcctVector3d(50, 30, -10));
            using var part = box.Cut(moved);
            part.ExportStep(path);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Le_a_placa_furada_e_o_volume_fecha_com_a_orientacao_corrigida()
    {
        StepResult r = StepMeshReader.Read(PlateStep());
        Assert.True(r.Ok, r.Error);
        TriangleMesh mesh = r.Mesh!;
        Assert.Equal(100, mesh.Size.X, 3);
        Assert.Equal(60, mesh.Size.Y, 3);
        Assert.Equal(20, mesh.Size.Z, 3);
        Assert.True(mesh.IsClosed);
        // A triangulação do OpenCASCADE vem com a parede do furo virada: sem o conserto o volume sairia 122 094
        Assert.True(r.Orientation!.FlippedTriangles > 0);
        Assert.InRange(MeshOrientation.SignedVolume(mesh), PlateVolume * 0.995, PlateVolume * 1.001);
        Assert.InRange(mesh.Volume, PlateVolume * 0.995, PlateVolume * 1.001);
    }

    [Fact]
    public void Malha_do_STEP_serve_para_a_varredura()
    {
        StepResult r = StepMeshReader.Read(PlateStep());
        Assert.True(r.Ok, r.Error);
        TriangleMesh model = r.Mesh!.PlacedTopAtZero();
        var tool = new CncStudio.Core.Cam.CamTool(1, 6, 8000, 1200, 400, "Bola D6");
        var levels = new CncStudio.Core.Cam.CamLevels(30, 5, 0, -20, 5);
        CncStudio.Core.Cam.OperationResult finish = SurfaceFinish.Raster(model, tool, levels, new SurfaceParams(3));
        Assert.True(finish.Ok, finish.Error);
    }

    [Fact]
    public void Arquivo_invalido_vira_erro_em_portugues()
    {
        StepResult garbage = StepMeshReader.Read(Encoding.ASCII.GetBytes("isto nao e um STEP\n"));
        Assert.False(garbage.Ok);
        Assert.Contains("STEP", garbage.Error);
        Assert.False(StepMeshReader.Read([]).Ok);
        Assert.False(StepMeshReader.Read(PlateStep(), 0).Ok);
    }

    [Fact]
    public void Unidade_em_polegadas_e_convertida_para_milimetros()
    {
        string text = Encoding.ASCII.GetString(PlateStep());
        const string mm = "( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) )";
        Assert.Contains(mm, text);
        string inch = "( CONVERSION_BASED_UNIT('INCH',#9901) LENGTH_UNIT() NAMED_UNIT(#9902) )";
        string extra = "\n#9901 = LENGTH_MEASURE_WITH_UNIT(LENGTH_MEASURE(25.4),#9903);\n#9902 = DIMENSIONAL_EXPONENTS(1.,0.,0.,0.,0.,0.,0.);\n#9903 = ( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) );";
        int at = text.IndexOf(mm, StringComparison.Ordinal);
        int end = text.IndexOf(';', at);
        string edited = text[..at] + inch + text[end..].Insert(1, extra);
        StepResult r = StepMeshReader.Read(Encoding.ASCII.GetBytes(edited));
        Assert.True(r.Ok, r.Error);
        // 100 "polegadas" = 2540 mm
        Assert.Equal(2540, r.Mesh!.Size.X, 1);
    }

    // Partes do STEP da placa exportado pelo OpenCASCADE: "#10 = ADVANCED_BREP_SHAPE_REPRESENTATION('',(#11,#15),#429);",
    // "#15 = MANIFOLD_SOLID_BREP('',#16);" e "#16 = CLOSED_SHELL('',(#17,...));"
    private sealed record PlateParts(string Text, Match Rep, Match Solid, Match Shell);

    private static PlateParts Parts()
    {
        string text = Encoding.ASCII.GetString(PlateStep());
        Match rep = Regex.Match(text, @"#(\d+) = ADVANCED_BREP_SHAPE_REPRESENTATION\('',\(([^)]*)\),#(\d+)\);");
        Match solid = Regex.Match(text, @"#(\d+) = MANIFOLD_SOLID_BREP\('',#(\d+)\);");
        Match shell = Regex.Match(text, @"#" + solid.Groups[2].Value + @" = CLOSED_SHELL\('',\(([^)]*)\)\);");
        Assert.True(rep.Success && solid.Success && shell.Success);
        return new PlateParts(text, rep, solid, shell);
    }

    private static string AddRecords(string text, string records)
    {
        int end = text.LastIndexOf("ENDSEC;", StringComparison.Ordinal);
        return text[..end] + records + "\n" + text[end..];
    }

    [Fact]
    public void Solido_com_superficie_auxiliar_le_so_o_solido()
    {
        // Como nos STEP do NIST com PMI: além do sólido, um SHELL_BASED_SURFACE_MODEL com OPEN_SHELL (aqui, uma face
        // repetida da própria placa). Lido junto, a malha ficaria com arestas de 3 triângulos e não fecharia.
        PlateParts p = Parts();
        string face = p.Shell.Groups[1].Value.Split(',')[0];
        string text = p.Text.Replace(p.Rep.Value, p.Rep.Value.Replace(p.Rep.Groups[2].Value, p.Rep.Groups[2].Value + ",#9002"));
        text = AddRecords(text, $"#9001 = OPEN_SHELL('',({face}));\n#9002 = SHELL_BASED_SURFACE_MODEL('',(#9001));");

        StepResult r = StepMeshReader.Read(Encoding.ASCII.GetBytes(text));
        Assert.True(r.Ok, r.Error);
        Assert.True(r.Mesh!.IsClosed);
        Assert.InRange(r.Mesh.Volume, PlateVolume * 0.995, PlateVolume * 1.001);
        Assert.Equal("Ignorei 1 superfície auxiliar; usei só o sólido.", r.Notice);

        // Sem superfície auxiliar: nenhum aviso
        Assert.Null(StepMeshReader.Read(PlateStep()).Notice);
    }

    [Fact]
    public void Sem_solido_le_as_superficies_como_antes()
    {
        // O sólido vira modelo de superfície aberto (sem uma das faces): continua lido, aberto e sem aviso de "ignorei"
        PlateParts p = Parts();
        string faces = p.Shell.Groups[1].Value;
        string text = p.Text
            .Replace(p.Solid.Value, $"#{p.Solid.Groups[1].Value} = SHELL_BASED_SURFACE_MODEL('',(#{p.Solid.Groups[2].Value}));")
            .Replace(p.Shell.Value, p.Shell.Value.Replace("CLOSED_SHELL", "OPEN_SHELL").Replace(faces, faces[(faces.IndexOf(',') + 1)..]))
            .Replace("ADVANCED_BREP_SHAPE_REPRESENTATION", "MANIFOLD_SURFACE_SHAPE_REPRESENTATION");

        StepResult r = StepMeshReader.Read(Encoding.ASCII.GetBytes(text));
        Assert.True(r.Ok, r.Error);
        Assert.False(MeshCheck.Of(r.Mesh!).IsClosed);
        Assert.Equal(100, r.Mesh!.Size.X, 3);
        Assert.Null(r.Notice);
    }

    [Fact]
    public void So_malha_facetada_le_com_aviso_de_volume_aproximado()
    {
        // AP242 tesselado (como o ftc_08 "-tg" do NIST): um tetraedro de triângulos no lugar do sólido B-rep
        PlateParts p = Parts();
        string text = p.Text
            .Replace(p.Solid.Value, "")
            .Replace(p.Rep.Value, p.Rep.Value
                .Replace("ADVANCED_BREP_SHAPE_REPRESENTATION", "TESSELLATED_SHAPE_REPRESENTATION")
                .Replace("#" + p.Solid.Groups[1].Value + ")", "#9102)"));
        text = AddRecords(text,
            "#9100 = COORDINATES_LIST('',4,((0.,0.,0.),(10.,0.,0.),(0.,10.,0.),(0.,0.,10.)));\n" +
            "#9101 = TRIANGULATED_FACE('',#9100,4,(),$,(),((1,3,2),(1,2,4),(2,3,4),(1,4,3)));\n" +
            "#9102 = TESSELLATED_SOLID('',(#9101),$);");

        StepResult r = StepMeshReader.Read(Encoding.ASCII.GetBytes(text));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(10, r.Mesh!.Size.X, 3);
        Assert.Equal(10, r.Mesh.Size.Z, 3);
        Assert.Equal("O arquivo só tem malha facetada, sem sólido: volume aproximado.", r.Notice);
    }

    [Fact]
    public void Dois_corpos_viram_uma_malha()
    {
        string path = Path.Combine(Path.GetTempPath(), "dois-" + Guid.NewGuid().ToString("N") + ".step");
        try
        {
            using var a = new OcctBox(10, 10, 10);
            using var b0 = new OcctBox(5, 5, 5);
            using var b = b0.Translate(new OcctVector3d(30, 0, 0));
            using var both = a.Fuse(b);
            both.ExportStep(path);
            StepResult r = StepMeshReader.Read(File.ReadAllBytes(path));
            Assert.True(r.Ok, r.Error);
            Assert.Equal(2, r.Orientation!.Components);
            Assert.Equal(1000 + 125, MeshOrientation.SignedVolume(r.Mesh!), 3);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
