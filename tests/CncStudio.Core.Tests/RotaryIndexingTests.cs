using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class RotaryIndexingTests
{
    private static readonly CamTool Tool = new(1, 6, 4000, 400, 150);

    private static CamOperation Face() => new("Bolso", Tool,
    [
        new ToolMove(MoveKind.Rapid, 10, 5, 20),
        new ToolMove(MoveKind.Feed, 10, 5, -3, Feed: 150),
        new ToolMove(MoveKind.Feed, 30, 5, -3, Feed: 400),
        new ToolMove(MoveKind.Rapid, 30, 5, 20),
    ], null, []);

    private static MachineProfile Machine(bool reversed = false)
    {
        MachineProfile m = MachineProfile.FourAxisTableA();
        m.Axes[0].Reversed = reversed;
        m.Axes[0].PivotY = 100;
        m.Axes[0].PivotZ = -50;
        return m;
    }

    [Fact]
    public void Translada_para_a_face_e_sobe_antes_de_girar()
    {
        OperationResult r = RotaryIndexing.Place(Face(), Machine(), "A", new IndexedFace(90, AxisToFace: 25));
        Assert.True(r.Ok, r.Error);
        var moves = r.Operation!.Moves;
        Assert.Equal(-50 + 25 + 20, moves[2].Z);          // topo da aproximação
        Assert.Equal(105, moves[2].Y);                     // 100 do pivô + 5
        Assert.Equal("A", moves[1].RotaryLetter);
        Assert.Equal(90, moves[1].RotaryAngle);
        Assert.True(moves[1].Z >= moves[2].Z);             // gira acima da peça
        Assert.Equal(moves[1].Z, moves[^1].Z);             // termina recuado
    }

    [Fact]
    public void Sentido_invertido_troca_o_sinal()
    {
        OperationResult r = RotaryIndexing.Place(Face(), Machine(reversed: true), "A", new IndexedFace(90, 25));
        Assert.Equal(-90, r.Operation!.Moves[1].RotaryAngle);
    }

    [Fact]
    public void Recusa_angulo_fora_do_curso_cabecote_e_modo_nativo()
    {
        Assert.False(RotaryIndexing.Place(Face(), Machine(), "A", new IndexedFace(400, 25)).Ok);
        Assert.False(RotaryIndexing.Place(Face(), MachineProfile.FiveAxisMixed(), "B", new IndexedFace(10, 25)).Ok);
        MachineProfile n = Machine();
        n.Mode = RotaryOutputMode.Native;
        Assert.False(RotaryIndexing.Place(Face(), n, "A", new IndexedFace(10, 25)).Ok);
        Assert.False(RotaryIndexing.Place(Face(), Machine(), "B", new IndexedFace(10, 25)).Ok);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Pos_escreve_palavra_A_uma_vez_e_o_simulador_aceita(CamDialect d)
    {
        CamOperation op = RotaryIndexing.Place(Face(), Machine(), "A", new IndexedFace(90, 25)).Operation!;
        string text = CamPost.Generate([op], new PostOptions(d));
        Assert.Single(text.Split('\n'), l => !l.StartsWith('(') && !l.StartsWith(';') && l.Contains("A90"));
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings());
        Assert.True(path.Diagnostics.All(x => x.Severity != DiagnosticSeverity.Error), text);
    }

    private static CamOperation Holes() =>
        CamOperations.Drilling(Tool, 20, [new Point2(10, 5), new Point2(30, 5)], DrillCycleKind.Peck, 0, -5, 2, peck: 2).Operation!;

    [Fact]
    public void Furos_vao_para_a_face_com_o_ciclo_deslocado_e_o_giro()
    {
        OperationResult r = RotaryIndexing.Place(Holes(), Machine(), "A", new IndexedFace(90, AxisToFace: 25));
        Assert.True(r.Ok, r.Error);
        CncStudio.Core.Cam.DrillCycle d = r.Operation!.Drill!;
        Assert.Equal("A", d.RotaryLetter);
        Assert.Equal(90, d.RotaryAngle);
        Assert.Equal(-50 + 25, d.TopZ);        // pivô + distância do eixo à face
        Assert.Equal(-50 + 25 - 5, d.BottomZ);
        Assert.Equal(105, d.Points[0].Y);       // pivô Y 100 + 5
        Assert.True(d.SafeZ >= d.RetractZ);    // gira acima do cilindro
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Furos_na_face_giram_uma_vez_antes_do_ciclo_e_o_simulador_aceita(CamDialect d)
    {
        CamOperation op = RotaryIndexing.Place(Holes(), Machine(), "A", new IndexedFace(90, 25)).Operation!;
        string text = CamPost.Generate([op], new PostOptions(d));
        string[] lines = text.Split('\n');
        int rotate = Array.FindIndex(lines, l => !l.StartsWith('(') && !l.StartsWith(';') && l.Contains("A90"));
        int cycle = Array.FindIndex(lines, l => l.Contains(d == CamDialect.Fanuc ? "G83" : "CYCLE83"));
        Assert.True(rotate >= 0 && cycle > rotate, text);
        Assert.Single(lines, l => !l.StartsWith('(') && !l.StartsWith(';') && l.Contains("A90"));

        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings());
        Assert.True(path.Diagnostics.All(x => x.Severity != DiagnosticSeverity.Error), text);
        // A broca desce até o fundo do furo na face (pivô Z -50 + 25 - 5)
        Assert.Contains(path.Moves, m => Math.Abs(m.End.Z - (-50 + 25 - 5)) < 1e-6);
    }
}
