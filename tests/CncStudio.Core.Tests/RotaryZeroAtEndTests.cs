using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Eixo rotativo a zero no fim do programa: só quando o programa usou o eixo e o perfil tem 0° no curso.</summary>
public class RotaryZeroAtEndTests
{
    private static readonly CamTool Tool = new(1, 6, 4000, 400, 150);

    private static CamOperation Flat() => new("Bolso", Tool,
    [
        new ToolMove(MoveKind.Rapid, 10, 5, 20),
        new ToolMove(MoveKind.Feed, 10, 5, -3, Feed: 150),
        new ToolMove(MoveKind.Feed, 30, 5, -3, Feed: 400),
        new ToolMove(MoveKind.Rapid, 30, 5, 20),
    ], null, []);

    private static MachineProfile Machine(double min = -360, double max = 360)
    {
        MachineProfile m = MachineProfile.FourAxisTableA();
        m.Axes[0].PivotY = 100;
        m.Axes[0].PivotZ = -50;
        m.Axes[0].MinAngle = min;
        m.Axes[0].MaxAngle = max;
        return m;
    }

    private static CamOperation Rotated(double angle, MachineProfile? machine = null) =>
        RotaryIndexing.Place(Flat(), machine ?? Machine(), "A", new IndexedFace(angle, 25)).Operation!;

    private static string[] Lines(string text) => text.Split('\n');

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Programa_sem_rotativo_continua_identico(CamDialect d)
    {
        string plain = CamPost.Generate([Flat()], new PostOptions(d));
        Assert.Equal(plain, CamPost.Generate([Flat()], new PostOptions(d, Machine: Machine())));
        string end = d == CamDialect.Fanuc ? "M5\nG91 G28 Z0\nG90\nM30\n%\n" : "M5\nG0 G53 Z0\nG90\nM30\n";
        Assert.EndsWith(end, plain);
        Assert.Empty(CamPost.RotaryAtEnd([Flat()]));
        Assert.Empty(CamPost.Check([Flat()], Machine()));
    }

    [Theory]
    [InlineData(CamDialect.Fanuc, "G0 A0.", "(EIXO A A ZERO - NAO VERIFICADO EM MAQUINA)", "G91 G28 Z0")]
    [InlineData(CamDialect.Siemens, "G0 A0", "; EIXO A A ZERO - NAO VERIFICADO EM MAQUINA", "G0 G53 Z0")]
    public void Volta_a_zero_depois_de_Z_na_referencia_e_antes_do_M30(CamDialect d, string zero, string comment, string zHome)
    {
        string text = CamPost.Generate([Rotated(90)], new PostOptions(d, Machine: Machine()));
        string[] lines = Lines(text);
        int z = Array.LastIndexOf(lines, zHome);
        int note = Array.IndexOf(lines, comment);
        int a0 = Array.IndexOf(lines, zero);
        int end = Array.IndexOf(lines, "M30");
        Assert.True(z >= 0 && note > z && a0 == note + 1 && end > a0, text);

        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings());
        Assert.True(path.Diagnostics.All(x => x.Severity != DiagnosticSeverity.Error), text);
        Assert.Equal(0, path.Modals[path.Moves[^1].ModalId].Rotary.A, 9);
    }

    [Fact]
    public void Aviso_de_nao_verificado_em_maquina_acompanha_o_retorno()
    {
        IReadOnlyList<string> warnings = CamPost.Check([Rotated(90)], Machine());
        Assert.Contains(warnings, w => w.Contains("volta a 0°") && w.Contains("NÃO VERIFICADO EM MÁQUINA"));
    }

    [Theory]
    [InlineData(10, 120)]   // curso não passa por 0°
    [InlineData(-120, -5)]
    public void Zero_fora_do_curso_do_perfil_nao_gira_e_avisa(double min, double max)
    {
        MachineProfile m = Machine(min, max);
        string text = CamPost.Generate([Rotated(90 * Math.Sign(max), m)], new PostOptions(CamDialect.Fanuc, Machine: m));
        Assert.DoesNotContain(Lines(text), l => l.StartsWith("G0 A0", StringComparison.Ordinal));
        Assert.Contains("(CONFIRA: EIXO A NAO VOLTA A ZERO (FORA DO CURSO OU SEM PERFIL))", Lines(text));
        Assert.Contains(CamPost.Check([Rotated(90 * Math.Sign(max), m)], m), w => w.Contains("NÃO volta a 0°") && w.Contains("fora do curso"));
    }

    [Fact]
    public void Sem_perfil_nao_gira_e_avisa()
    {
        string text = CamPost.Generate([Rotated(90)], new PostOptions(CamDialect.Fanuc));
        Assert.DoesNotContain(Lines(text), l => l.StartsWith("G0 A0", StringComparison.Ordinal));
        Assert.Contains(CamPost.Check([Rotated(90)]), w => w.Contains("NÃO volta a 0°") && w.Contains("não informa"));
    }

    [Fact]
    public void Programa_que_ja_termina_em_zero_nao_ganha_linha()
    {
        string text = CamPost.Generate([Rotated(90), Rotated(0)], new PostOptions(CamDialect.Fanuc, Machine: Machine()));
        Assert.Empty(CamPost.RotaryAtEnd([Rotated(90), Rotated(0)]));
        Assert.Single(Lines(text), l => l.Contains("A0."));
        Assert.DoesNotContain("A ZERO", text);
    }

    [Fact]
    public void Furacao_na_face_girada_tambem_volta()
    {
        CamOperation holes = CamOperations.Drilling(Tool, 20, [new Point2(10, 5)], DrillCycleKind.Drill, 0, -5, 2).Operation!;
        CamOperation placed = RotaryIndexing.Place(holes, Machine(), "A", new IndexedFace(-45, 25)).Operation!;
        Assert.Equal([("A", -45.0)], CamPost.RotaryAtEnd([placed]));
        Assert.Contains("G0 A0.", Lines(CamPost.Generate([placed], new PostOptions(CamDialect.Fanuc, Machine: Machine()))));
    }

    [Fact]
    public void Furacao_3mais2_volta_os_dois_eixos()
    {
        // junção do 3+2 (#124) com o retorno a zero (#125): o segundo eixo da furação também conta
        CamOperation holes = CamOperations.Drilling(Tool, 20, [new Point2(10, 5)], DrillCycleKind.Drill, 0, -5, 2).Operation!;
        CamOperation placed = holes with { Drill = holes.Drill! with { RotaryLetter = "A", RotaryAngle = -45, RotaryLetter2 = "C", RotaryAngle2 = 90 } };
        Assert.Equal([("C", 90.0), ("A", -45.0)], CamPost.RotaryAtEnd([placed]));
    }

    [Fact]
    public void Trabalho_usa_o_perfil_da_maquina_para_o_retorno()
    {
        var job = new CamJob { Name = "PECA", Dialect = CamDialect.Fanuc, Stock = new JobStock(-5, -5, 70, 50, 20) };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, new CamTool(1, 10, 3000, 500, 200), new CamLevels(50, 2, 0, -6, 3))
        {
            ContourIndex = 0, ContourParams = new ContourParams(ContourSide.OnLine), Face = new RotaryFaceSpec("A", 90, 25),
        });
        JobBuild build = CamJobBuilder.Build(job, Machine());
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("G0 A0.", Lines(build.Program));
        Assert.Contains(build.Warnings, w => w.Contains("NÃO VERIFICADO EM MÁQUINA") && w.Contains("volta a 0°"));
    }
}
