using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Variáveis, saltos, laços, subprogramas e repetições.</summary>
public class InterpreterTests
{
    private static ToolPath Fanuc(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc), settings);

    private static ToolPath Siemens(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Siemens), settings);

    private static double[] EndX(ToolPath path) => path.Moves.Select(m => Math.Round(m.End.X, 6)).ToArray();

    private static string Samples(string relative) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", relative));

    // ───────── Fanuc ─────────

    [Fact]
    public void Fanuc_VariavelEmEndereco()
    {
        ToolPath path = Fanuc("G00 X1\n#1=10\nG01 X#1 Y-[#1/2] F100");

        Assert.Equal(new[] { 1.0, 10.0 }, EndX(path));
        Assert.Equal(-5, path.Moves[1].End.Y, 9);
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void Fanuc_While()
    {
        ToolPath path = Fanuc("""
            #1=0
            WHILE [#1 LT 3] DO1
            G01 X[#1+1] F100
            #1=#1+1
            END1
            M30
            """);

        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, EndX(path));
        Assert.Equal(3, path.MovesForLine(3).Count); // a mesma linha gerou 3 movimentos
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void Fanuc_IfGotoPulaBlocos()
    {
        ToolPath path = Fanuc("""
            #1=5
            IF [#1 GT 3] GOTO 100
            G01 X99 F100
            N100 G01 X10 F100
            """);

        Assert.Equal(new[] { 10.0 }, EndX(path));
    }

    [Fact]
    public void Fanuc_IfThen()
    {
        ToolPath path = Fanuc("#1=1\nIF [#1 EQ 1] THEN #2=7\nIF [#1 EQ 2] THEN #2=9\nG01 X#2 F100");

        Assert.Equal(new[] { 7.0 }, EndX(path));
    }

    [Fact]
    public void Fanuc_SubprogramaNoMesmoArquivoComRepeticao()
    {
        ToolPath path = Fanuc("""
            O1000
            G00 X0 Y0
            M98 P2000 L2
            M30
            O2000
            G91 G01 X10 F100
            G90
            M99
            """);

        Assert.Equal(new[] { 10.0, 20.0 }, EndX(path));
        Assert.Empty(path.Diagnostics); // subprograma depois do M30 não é "código morto"
    }

    [Fact]
    public void Fanuc_G65PassaArgumentos()
    {
        ToolPath path = Fanuc("""
            O0001
            #1=99
            G65 P9010 A5. B2.
            G01 X#1 F100
            M30
            O9010
            G01 X#1 Y#2 F100
            M99
            """);

        Assert.Equal(5, path.Moves[0].End.X, 9);
        Assert.Equal(2, path.Moves[0].End.Y, 9);
        Assert.Equal(99, path.Moves[1].End.X, 9); // #1 local do principal não foi afetado
    }

    [Fact]
    public void Fanuc_M99ComPVoltaParaOBlocoIndicado()
    {
        ToolPath path = Fanuc("""
            O1
            M98 P2
            G01 X99 F100
            N50 G01 X50 F100
            M30
            O2
            M99 P50
            """);

        Assert.Equal(new[] { 50.0 }, EndX(path));
    }

    [Fact]
    public void Fanuc_LacoInfinitoEhInterrompido()
    {
        var settings = new MachineSettings { MaxExecutedBlocks = 1000 };

        ToolPath path = Fanuc("N10 G91 G01 X1 F100\nGOTO10", settings);

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("laço infinito"));
        Assert.InRange(path.Moves.Count, 400, 600);
    }

    [Fact]
    public void Fanuc_AlarmeParaAExecucao()
    {
        ToolPath path = Fanuc("G01 X1 F100\n#3000=1 (ERRO)\nG01 X2");

        Assert.Single(path.Moves);
        Assert.Contains(path.Diagnostics, d => d.Message.Contains("#3000"));
    }

    [Fact]
    public void Fanuc_EndSemDoEhErro()
    {
        ToolPath path = Fanuc("G01 X1 F100\nEND1");

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Fanuc_ExemploEspiralWhile()
    {
        ToolPath path = Fanuc(Samples("macros/ESPIRAL_WHILE.nc"));

        Assert.DoesNotContain(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(path.Diagnostics, d => d.Message.Contains("pulada"));

        var spiral = path.Moves.Where(m => m.Motion == MotionMode.Linear && m.End.Z < 0).ToList();
        Assert.InRange(spiral.Count, 2870, 2890); // 8 voltas de 360 passos

        double Radius(Move m) => Math.Sqrt(m.End.X * m.End.X + m.End.Y * m.End.Y);
        Assert.Equal(48, Radius(spiral[0]), 1);
        Assert.InRange(Radius(spiral[^1]), 0, 0.1); // termina no centro
    }

    // ───────── Siemens ─────────

    [Fact]
    public void Siemens_ParametrosREIcAc()
    {
        ToolPath path = Siemens("R1=10 R2=R1*2\nG1 X=R2 F100\nX=IC(5)\nG91 X=AC(3)");

        Assert.Equal(new[] { 20.0, 25.0, 3.0 }, EndX(path));
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void Siemens_For()
    {
        ToolPath path = Siemens("FOR R1=1 TO 3\nG1 X=R1*10 F100\nENDFOR\nM30");

        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, EndX(path));
    }

    [Fact]
    public void Siemens_WhileRepeatLoop()
    {
        ToolPath path = Siemens("""
            R1=0
            WHILE R1<2
            R1=R1+1
            G1 X=R1 F100
            ENDWHILE
            REPEAT
            R1=R1+1
            G1 X=R1
            UNTIL R1>=4
            LOOP
            R1=R1+1
            G1 X=R1
            IF R1>=6 GOTOF FIM
            ENDLOOP
            FIM:
            M30
            """);

        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, EndX(path));
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void Siemens_IfElseifElse()
    {
        string Program(int value) => $"""
            R1={value}
            IF R1==1
            G1 X10 F100
            ELSEIF R1==2
            G1 X20 F100
            ELSE
            G1 X30 F100
            ENDIF
            G1 Y5
            """;

        Assert.Equal(10, Siemens(Program(1)).Moves[0].End.X, 9);
        Assert.Equal(20, Siemens(Program(2)).Moves[0].End.X, 9);
        Assert.Equal(30, Siemens(Program(3)).Moves[0].End.X, 9);
        Assert.Equal(2, Siemens(Program(3)).Moves.Count);
    }

    [Fact]
    public void Siemens_DefEMsg()
    {
        ToolPath path = Siemens("DEF REAL AA=2.5, BB\nMSG(\"AA=\"<<AA<<\" mm\")\nG1 X=AA+BB F100");

        Assert.Equal(new[] { 2.5 }, EndX(path));
        ProgramMessage message = Assert.Single(path.Messages);
        Assert.Equal("AA=2.5 mm", message.Text);
        Assert.Equal("AA=2.5 mm", path.MessageAt(0));
    }

    [Fact]
    public void Siemens_EixoComExpressaoComecandoPorNumero()
    {
        ToolPath path = Siemens("DEF INT NN=2\nG1 X=10+NN*20 Z=-8-NN*8 F100");

        Assert.Empty(path.Diagnostics);
        Assert.Equal(50, path.Moves[^1].End.X, 6);
        Assert.Equal(-24, path.Moves[^1].End.Z, 6);
    }

    [Fact]
    public void Siemens_DefComNomeDeUmaLetraEhErro()
    {
        ToolPath path = Siemens("DEF INT I\nG1 X10 F100");

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("duas letras"));
    }

    [Fact]
    public void Siemens_RepeatTrechoEntreRotulos()
    {
        ToolPath path = Siemens("""
            INICIO:
            G91 G1 X5 F100
            FIM:
            G90
            REPEAT INICIO FIM P2
            M30
            """);

        Assert.Equal(new[] { 5.0, 10.0, 15.0 }, EndX(path));
    }

    [Fact]
    public void Siemens_SubprogramaExternoComRepeticao()
    {
        var settings = new MachineSettings
        {
            LoadSubprogram = name => name == "L10" ? "G91 G1 X5 F100\nG90\nM17" : null,
        };

        ToolPath path = Siemens("G0 X0\nL10 P2\nM30", settings);

        Assert.Equal(new[] { 5.0, 10.0 }, EndX(path));
        Assert.All(path.Moves, m => Assert.Equal(2, m.LineNumber)); // movimentos ficam na linha da chamada
    }

    [Fact]
    public void Siemens_SubprogramaPorNome()
    {
        var settings = new MachineSettings
        {
            LoadSubprogram = name => name == "FURO" ? "G1 Z-5 F100\nG0 Z5\nRET" : null,
        };

        ToolPath path = Siemens("G0 X10 Y10 Z5\nFURO\nG0 X20\nFURO\nM30", settings);

        Assert.Equal(2, path.Moves.Count(m => m.End.Z < 0));
    }

    [Fact]
    public void SubprogramaInexistenteEhErro()
    {
        ToolPath path = Siemens("L99\nM30");

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("L99"));
    }

    [Fact]
    public void Fanuc_ExemploSubprogramaEMacro()
    {
        ToolPath path = Fanuc(Samples("macros/SUBPROGRAMA.nc"));

        Assert.Empty(path.Diagnostics);
        // 3 bolsões (Z-3) e 6 furos em círculo (Z-4)
        Assert.Equal(3, path.Moves.Count(m => Math.Abs(m.End.Z + 3) < 1e-6 && m.Start.Z > m.End.Z));
        Assert.Equal(6, path.Moves.Count(m => Math.Abs(m.End.Z + 4) < 1e-6));
        Assert.Contains(path.Moves, m => Math.Abs(m.End.X - 60) < 1e-6 && Math.Abs(m.End.Y - 45) < 1e-6); // furo a 0°
    }

    [Fact]
    public void Siemens_ExemploChamadaPorNome()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "samples", "macros");
        var settings = new MachineSettings
        {
            LoadSubprogram = name => File.Exists(Path.Combine(folder, name + ".SPF")) ? File.ReadAllText(Path.Combine(folder, name + ".SPF")) : null,
        };

        ToolPath path = Siemens(Samples("macros/CHAMA_FURO.mpf"), settings);

        Assert.Empty(path.Diagnostics);
        Assert.Equal(5, path.Moves.Count(m => Math.Abs(m.End.Z + 6) < 1e-6));
        Assert.Equal(5, path.Messages.Count(m => m.Text.StartsWith("FURO")));
        Assert.Equal("FURO 3 DE 5", path.Messages[2].Text);
    }

    [Fact]
    public void Siemens_ExemploChaveta()
    {
        ToolPath path = Siemens(Samples("macros/CHAVETA.mpf"));

        Assert.DoesNotContain(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(path.Diagnostics, d => d.Message.Contains("pulada"));

        // Desce de 0,1 em 0,1 até Z-3.1
        Assert.Equal(-3.1, path.Moves.Min(m => m.End.Z), 6);
        int passes = path.Moves.Count(m => m.Motion == MotionMode.CounterClockwiseArc);
        Assert.Equal(32, passes); // Z0 ... Z-3.0 (31 passadas) + Z-3.1

        // O M30 da N42 deixa os REPEAT de baixo sem executar
        Assert.Contains(path.Diagnostics, d => d.Message.Contains("nunca são executados"));
        Assert.True(path.Messages.Count > 1);
    }
}
