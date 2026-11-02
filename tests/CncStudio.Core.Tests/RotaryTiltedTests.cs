using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>3+2 na mesa dupla (A basculante carregando C): posição, ordem dos giros, cursos do perfil, pós e simulador.</summary>
public class RotaryTiltedTests
{
    private static readonly CamTool Tool = new(1, 6, 4000, 400, 150);

    private static CamOperation Face() => new("Bolso", Tool,
    [
        new ToolMove(MoveKind.Rapid, 10, 5, 20),
        new ToolMove(MoveKind.Feed, 10, 5, -3, Feed: 150),
        new ToolMove(MoveKind.Feed, 30, 5, -3, Feed: 400),
        new ToolMove(MoveKind.Rapid, 30, 5, 20),
    ], null, []);

    private static CamOperation Holes() =>
        CamOperations.Drilling(Tool, 20, [new Point2(10, 5), new Point2(30, 5)], DrillCycleKind.Peck, 0, -5, 2, peck: 2).Operation!;

    // Modelo pronto: A de -30° a 110°, C de -360° a 360°
    private static MachineProfile Machine(bool reverseA = false, bool reverseC = false)
    {
        MachineProfile m = MachineProfile.FiveAxisTableTable();
        m.Axes[0].Reversed = reverseA;
        m.Axes[0].PivotY = 100;
        m.Axes[0].PivotZ = -50;
        m.Axes[1].Reversed = reverseC;
        return m;
    }

    private static bool IsCode(string l) => !l.StartsWith('(') && !l.StartsWith(';');

    [Fact]
    public void Gira_C_e_depois_A_na_altura_segura_e_translada_para_a_face()
    {
        OperationResult r = RotaryIndexing.PlaceTilted(Face(), Machine(), new TiltedFace(Tilt: 90, Rotate: 45, AxisToFace: 25));
        Assert.True(r.Ok, r.Error);
        var moves = r.Operation!.Moves;
        Assert.Equal(("C", 45.0), (moves[1].RotaryLetter, moves[1].RotaryAngle));
        Assert.Equal(("A", 90.0), (moves[2].RotaryLetter, moves[2].RotaryAngle));
        Assert.Equal(-50 + 25 + 20, moves[3].Z);           // topo da aproximação: pivô + distância à face + 20
        Assert.Equal(105, moves[3].Y);                      // pivô Y 100 + 5
        Assert.True(moves[1].Z >= moves[3].Z && moves[2].Z == moves[1].Z); // os dois giros acima da peça
        Assert.Equal(moves[1].Z, moves[^1].Z);              // termina recuado
        Assert.Equal("Bolso (A90 C45)", r.Operation.Name);
    }

    [Fact]
    public void Sempre_avisa_que_nao_foi_verificado_em_maquina()
    {
        Assert.Contains(RotaryIndexing.NotMachineVerified, RotaryIndexing.PlaceTilted(Face(), Machine(), new TiltedFace(30, 0, 25)).Operation!.Warnings);
        Assert.Contains(RotaryIndexing.NotMachineVerified, RotaryIndexing.PlaceTilted(Holes(), Machine(), new TiltedFace(30, 0, 25)).Operation!.Warnings);
        Assert.Contains("NÃO VERIFICADO EM MÁQUINA", RotaryIndexing.NotMachineVerified);
    }

    [Fact]
    public void Sentido_invertido_troca_o_sinal_de_cada_eixo()
    {
        var moves = RotaryIndexing.PlaceTilted(Face(), Machine(reverseA: true, reverseC: true), new TiltedFace(-20, 45, 25)).Operation!.Moves;
        Assert.Equal(-45, moves[1].RotaryAngle);
        Assert.Equal(20, moves[2].RotaryAngle);
    }

    [Theory]
    [InlineData(110, 0, true)]     // limite exato do A
    [InlineData(-30, 360, true)]   // limites exatos do A e do C
    [InlineData(111, 0, false)]    // A acima do curso
    [InlineData(-31, 0, false)]    // A abaixo do curso
    [InlineData(0, 361, false)]    // C acima do curso
    [InlineData(0, -400, false)]   // C abaixo do curso
    public void Limites_dos_eixos_vem_do_perfil(double a, double c, bool ok)
    {
        OperationResult r = RotaryIndexing.PlaceTilted(Face(), Machine(), new TiltedFace(a, c, 25));
        Assert.Equal(ok, r.Ok);
        if (!ok)
        {
            Assert.Contains("fora do curso", r.Error);
        }
    }

    [Fact]
    public void Limite_do_A_conta_o_sentido_invertido()
    {
        // Invertido: A90 no desenho vira A-90 na máquina, fora de -30° a 110°
        Assert.False(RotaryIndexing.PlaceTilted(Face(), Machine(reverseA: true), new TiltedFace(90, 0, 25)).Ok);
        Assert.True(RotaryIndexing.PlaceTilted(Face(), Machine(reverseA: true), new TiltedFace(20, 0, 25)).Ok);
    }

    [Fact]
    public void Recusa_o_que_nao_e_mesa_dupla_no_modo_calculado()
    {
        Assert.False(RotaryIndexing.PlaceTilted(Face(), MachineProfile.FourAxisTableA(), new TiltedFace(10, 10, 25)).Ok);
        Assert.False(RotaryIndexing.PlaceTilted(Face(), MachineProfile.FiveAxisMixed(), new TiltedFace(10, 10, 25)).Ok);
        Assert.False(RotaryIndexing.PlaceTilted(Face(), MachineProfile.ThreeAxis(), new TiltedFace(10, 10, 25)).Ok);
        MachineProfile native = Machine();
        native.Mode = RotaryOutputMode.Native;
        Assert.False(RotaryIndexing.PlaceTilted(Face(), native, new TiltedFace(10, 10, 25)).Ok);
        MachineProfile cFirst = Machine();
        cFirst.Axes.Reverse(); // C carregando A: o basculante precisa ser o primeiro
        Assert.False(RotaryIndexing.PlaceTilted(Face(), cFirst, new TiltedFace(10, 10, 25)).Ok);
        Assert.False(RotaryIndexing.PlaceTilted(Face(), Machine(), new TiltedFace(double.NaN, 10, 25)).Ok);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Pos_gira_C_antes_de_A_uma_vez_e_o_simulador_aceita(CamDialect d)
    {
        CamOperation op = RotaryIndexing.PlaceTilted(Face(), Machine(), new TiltedFace(90, 45, 25)).Operation!;
        string text = CamPost.Generate([op], new PostOptions(d));
        string[] lines = text.Split('\n');
        int c = Array.FindIndex(lines, l => IsCode(l) && l.Contains("C45"));
        int a = Array.FindIndex(lines, l => IsCode(l) && l.Contains("A90"));
        Assert.True(c >= 0 && a > c, text);
        Assert.Single(lines, l => IsCode(l) && l.Contains("C45"));
        Assert.Single(lines, l => IsCode(l) && l.Contains("A90"));
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings());
        Assert.True(path.Diagnostics.All(x => x.Severity != DiagnosticSeverity.Error), text);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Furos_3mais2_giram_C_e_A_antes_do_ciclo(CamDialect d)
    {
        OperationResult r = RotaryIndexing.PlaceTilted(Holes(), Machine(), new TiltedFace(90, 45, 25));
        Assert.True(r.Ok, r.Error);
        Cam.DrillCycle cycle = r.Operation!.Drill!;
        Assert.Equal(("A", 90.0, "C", 45.0), (cycle.RotaryLetter, cycle.RotaryAngle, cycle.RotaryLetter2, cycle.RotaryAngle2));
        Assert.Equal(-50 + 25, cycle.TopZ);
        Assert.True(cycle.SafeZ >= cycle.RetractZ);

        string text = CamPost.Generate([r.Operation], new PostOptions(d));
        string[] lines = text.Split('\n');
        int c = Array.FindIndex(lines, l => IsCode(l) && l.Contains("C45"));
        int a = Array.FindIndex(lines, l => IsCode(l) && l.Contains("A90"));
        int drill = Array.FindIndex(lines, l => l.Contains(d == CamDialect.Fanuc ? "G83" : "CYCLE83"));
        Assert.True(c >= 0 && a > c && drill > a, text);
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings());
        Assert.True(path.Diagnostics.All(x => x.Severity != DiagnosticSeverity.Error), text);
    }

    [Fact]
    public void Furo_de_um_eixo_so_continua_igual()
    {
        MachineProfile four = MachineProfile.FourAxisTableA();
        string text = CamPost.Generate([RotaryIndexing.Place(Holes(), four, "A", new IndexedFace(90, 25)).Operation!], new PostOptions(CamDialect.Fanuc));
        Assert.DoesNotContain(text.Split('\n'), l => l.StartsWith("G0 C", StringComparison.Ordinal));
        Assert.Single(text.Split('\n'), l => l.StartsWith("G0 A90", StringComparison.Ordinal));
    }

    private static Point3 Apply(double[] m, Point3 p) => new(
        (m[0] * p.X) + (m[4] * p.Y) + (m[8] * p.Z) + m[12],
        (m[1] * p.X) + (m[5] * p.Y) + (m[9] * p.Z) + m[13],
        (m[2] * p.X) + (m[6] * p.Y) + (m[10] * p.Z) + m[14]);

    [Theory]
    [InlineData(90, 45)]
    [InlineData(-20, 170)]
    [InlineData(0, -90)]
    public void Simulador_com_C_a_matriz_e_o_contrario_de_ToPart(double a, double c)
    {
        MachineProfile m = Machine(reverseC: true);
        m.Axes[1].PivotX = 7;
        m.Axes[1].PivotY = 100;
        RotaryPartFrame frame = RotaryPartFrame.From(m)!;
        Assert.True(frame.HasA && frame.HasC);
        var offset = new Point3(100, 50, 10);
        var angles = new RotaryAngles(a, 0, c);
        var part = new Point3(113, 61, -7);
        Point3 back = frame.ToPart(Apply(frame.ToMachineMatrix(offset, angles), part), offset, angles);
        Assert.Equal(part.X, back.X, 6);
        Assert.Equal(part.Y, back.Y, 6);
        Assert.Equal(part.Z, back.Z, 6);
    }

    [Fact]
    public void C_gira_em_torno_do_seu_pivo_pela_mao_direita()
    {
        MachineProfile m = Machine();
        m.Axes[1].PivotX = 10;
        m.Axes[1].PivotY = 20;
        RotaryPartFrame frame = RotaryPartFrame.From(m)!;
        // Ponto 5 mm à direita do pivô; C90 leva para 5 mm à frente (sentido anti-horário visto de cima)
        Point3 machine = Apply(frame.ToMachineMatrix(Point3.Origin, new RotaryAngles(0, 0, 90)), new Point3(15, 20, 3));
        Assert.Equal(10, machine.X, 6);
        Assert.Equal(25, machine.Y, 6);
        Assert.Equal(3, machine.Z, 6);
    }

    [Fact]
    public void Perfil_sem_C_ignora_o_angulo_de_C_como_antes()
    {
        RotaryPartFrame frame = RotaryPartFrame.From(MachineProfile.FourAxisTableA())!;
        Assert.False(frame.HasC);
        var p = new Point3(15, 20, 3);
        Assert.Equal(p, frame.ToPart(p, Point3.Origin, new RotaryAngles(0, 0, 90)));
    }
}
