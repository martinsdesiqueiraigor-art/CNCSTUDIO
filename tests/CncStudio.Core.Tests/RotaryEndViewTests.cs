using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class RotaryEndViewTests
{
    private static readonly RotaryPartFrame Frame = new(AY: 0, AZ: -25, AReversed: false, BX: 0, BZ: -25, BReversed: false);

    private static readonly SetupStock Bar = new() { Kind = SetupStockKind.Round, Axis = SetupAxis.Auto, X = 0, Height = 60, Diameter = 50 };

    // Corte reto sem giro, gira A90 e corta de novo
    private const string Text = "G21 G90 G54\nG00 X0 Y0 Z5\nG01 Z-2 F100\nG01 X20\nG00 Z10\nG00 A90.\nG00 X0 Y0 Z5\nG01 Z-2 F100\nG01 X20\nG00 Z10";

    private static ToolPath PathOf(string text) =>
        MachineExecutor.Execute(GCodeParser.Parse(text, Dialect.Fanuc), new MachineSettings { PartFrame = Frame });

    [Fact]
    public void SemGiroNaoTemVista()
    {
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse("G21 G90 G54\nG00 X0 Y0 Z5\nG01 Z-2 F100", Dialect.Fanuc), new MachineSettings { PartFrame = Frame });
        Assert.Null(RotaryEndView.Build(path, Bar, 2, null, 6));
    }

    [Fact]
    public void ContornoEhOCirculoDoBrutoNoPivo()
    {
        ToolPath path = PathOf(Text);
        RotaryEndView v = RotaryEndView.Build(path, Bar, 0, null, 6)!;
        Assert.Equal('A', v.AxisLetter);
        Assert.Equal((0, -25), v.Center);
        Assert.All(v.Outline, p => Assert.Equal(25, Math.Sqrt(Math.Pow(p.U - 0, 2) + Math.Pow(p.V + 25, 2)), 6));
        Assert.Equal(72, v.Outline.Count);
        // Parado: a marca aponta para cima
        Assert.Equal(0, v.Mark.U2, 6);
        Assert.Equal(0, v.Mark.V2, 6); // topo = pivô Z (-25) + 25
    }

    [Fact]
    public void ComA90AMarcaGiraNoventaGraus()
    {
        ToolPath path = PathOf(Text);
        RotaryEndView still = RotaryEndView.Build(path, Bar, 4, null, 6)!;
        RotaryEndView turned = RotaryEndView.Build(path, Bar, path.Moves.Count, null, 6)!;
        Assert.Equal(0, still.Angle, 6);
        Assert.Equal(90, turned.Angle, 6);
        double dist = Math.Sqrt(Math.Pow(still.Mark.U2 - turned.Mark.U2, 2) + Math.Pow(still.Mark.V2 - turned.Mark.V2, 2));
        Assert.Equal(25 * Math.Sqrt(2), dist, 4);
        // O centro não muda
        Assert.Equal(still.Center, turned.Center);
    }

    [Fact]
    public void PercursoTerminaNaPontaDaFerramenta()
    {
        ToolPath path = PathOf(Text);
        RotaryEndView v = RotaryEndView.Build(path, Bar, path.Moves.Count, null, 6)!;
        EndViewSegment last = v.Path[^1];
        Assert.Equal(v.Tool.U, last.U2, 4);
        Assert.Equal(v.Tool.V, last.V2, 4);
        Assert.NotEmpty(v.Path);
    }

    [Fact]
    public void CorteAntesDoGiroFicaNaPecaEGiraJunto()
    {
        ToolPath path = PathOf(Text);
        // Corte feito a A0 na altura Z-2: ponto do topo da peça (Z -2); depois do giro de 90° ele sai do topo
        RotaryEndView before = RotaryEndView.Build(path, Bar, 4, null, 6)!;
        RotaryEndView after = RotaryEndView.Build(path, Bar, path.Moves.Count, null, 6)!;
        EndViewSegment first = before.Path.First(s => !s.Rapid);
        EndViewSegment sameCutLater = after.Path.First(s => !s.Rapid);
        Assert.NotEqual(first.V1, sameCutLater.V1, 3); // mudou de lugar com a peça
    }

    [Fact]
    public void SemBrutoNaoTemContornoMasTemPercurso()
    {
        ToolPath path = PathOf(Text);
        RotaryEndView v = RotaryEndView.Build(path, null, path.Moves.Count, null, 6)!;
        Assert.Empty(v.Outline);
        Assert.NotEmpty(v.Path);
        Assert.True(v.FitRadius >= 5);
    }
}
