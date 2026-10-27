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
}
