using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Animação da ferramenta dentro de um passo (percurso e retornos, sem saltar para o fim).</summary>
public class StepAnimationTests
{
    private static IReadOnlyList<Move> Moves(string program) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc)).Moves;

    [Fact]
    public void ComecoDoPassoFicaNoPrimeiroMovimentoSemTerminarNada()
    {
        IReadOnlyList<Move> moves = Moves("G01 X10 F100\nX20\nX40");

        StepPose pose = StepAnimation.At(moves, 0, moves.Count, 0);

        Assert.Equal(new StepPose(0, 0, 0), pose);
    }

    [Fact]
    public void FimDoPassoTerminaTodosOsMovimentos()
    {
        IReadOnlyList<Move> moves = Moves("G01 X10 F100\nX20\nX40");

        StepPose pose = StepAnimation.At(moves, 0, moves.Count, 1);

        Assert.Equal(moves.Count, pose.Completed);
        Assert.Equal(moves.Count - 1, pose.MoveIndex);
        Assert.Equal(1, pose.Fraction);
    }

    [Fact]
    public void MeioDeUmaRetaLongaAnimaAFracaoDoMovimento()
    {
        IReadOnlyList<Move> moves = Moves("G01 X100 F100");

        StepPose pose = StepAnimation.At(moves, 0, 1, 0.25);

        Assert.Equal(0, pose.Completed);
        Assert.Equal(0.25, pose.Fraction, 6);
        Assert.Equal(25, moves[0].PointAt(pose.Fraction).X, 6);
    }

    [Fact]
    public void RapidoDoRetornoPassaMaisDepressaQueOCorte()
    {
        // corte de 40 mm e retorno rápido de 40 mm: o corte ocupa RapidSpeedup vezes mais tempo
        IReadOnlyList<Move> moves = Moves("G01 X40 F100\nG00 X0");
        double cutShare = StepAnimation.RapidSpeedup / (StepAnimation.RapidSpeedup + 1);

        StepPose beforeEndOfCut = StepAnimation.At(moves, 0, 2, cutShare - 0.01);
        StepPose afterEndOfCut = StepAnimation.At(moves, 0, 2, cutShare + 0.01);

        Assert.Equal(0, beforeEndOfCut.MoveIndex);
        Assert.Equal(1, afterEndOfCut.MoveIndex);
        Assert.Equal(1, afterEndOfCut.Completed);
    }

    [Fact]
    public void PassoSemMovimentosNaoQuebra()
    {
        IReadOnlyList<Move> moves = Moves("G01 X10 F100");

        StepPose pose = StepAnimation.At(moves, 1, 1, 0.5);

        Assert.Equal(0, pose.Completed);
    }

    [Fact]
    public void AndamentoForaDeFaixaFicaLimitado()
    {
        IReadOnlyList<Move> moves = Moves("G01 X10 F100\nX20");

        Assert.Equal(0, StepAnimation.At(moves, 0, 2, -3).Completed);
        Assert.Equal(2, StepAnimation.At(moves, 0, 2, 9).Completed);
    }
}
