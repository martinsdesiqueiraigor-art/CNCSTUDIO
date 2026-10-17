using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Execução bloco a bloco (base do depurador de macros).</summary>
public class ExecutionSessionTests
{
    private static ExecutionSession Fanuc(string program, MachineSettings? settings = null) =>
        ExecutionSession.Start(GCodeParser.Parse(program, Dialect.Fanuc), settings);

    private static ExecutionSession Siemens(string program) =>
        ExecutionSession.Start(GCodeParser.Parse(program, Dialect.Siemens));

    private const string Laco = """
        #1=0
        WHILE [#1 LT 3] DO1
        G01 X[#1+1] F100
        #1=#1+1
        END1
        M30
        """;

    [Fact]
    public void ComecaSemNadaExecutadoEComOPrimeiroBlocoPronto()
    {
        ExecutionSession session = Fanuc(Laco);
        Assert.False(session.IsFinished);
        Assert.Equal(1, session.NextLine);
        Assert.Null(session.LastBlock);
        Assert.Equal(0, session.MoveCount);
        Assert.Empty(session.Variables);
    }

    [Fact]
    public void PassoAPassoSegueOLaco()
    {
        ExecutionSession session = Fanuc(Laco);
        var lines = new List<int>();
        while (session.Step() is { } block)
        {
            lines.Add(block.Line);
        }

        // 1, depois 3 voltas de (2, 3, 4, 5), a checagem final do WHILE e o M30
        Assert.Equal([1, 2, 3, 4, 5, 2, 3, 4, 5, 2, 3, 4, 5, 2, 6], lines);
        Assert.True(session.IsFinished);
        Assert.Null(session.NextLine);
        Assert.Null(session.Step());
    }

    [Fact]
    public void InformaAsVariaveisAlteradasEmCadaBloco()
    {
        ExecutionSession session = Fanuc(Laco);

        ExecutedBlock first = session.Step()!;
        Assert.Equal(new VariableChange("#1", null, 0), Assert.Single(first.Changes));

        ExecutedBlock whileCheck = session.Step()!;
        Assert.Empty(whileCheck.Changes);

        ExecutedBlock move = session.Step()!;
        Assert.Empty(move.Changes);
        Assert.Equal(0, move.FirstMove);
        Assert.Equal(1, move.MoveCount);

        ExecutedBlock increment = session.Step()!;
        Assert.Equal(new VariableChange("#1", 0, 1), Assert.Single(increment.Changes));
        Assert.Equal(1, session.Variables["#1"]);
    }

    [Fact]
    public void ParaAntesDaLinhaDoPontoDeParadaEmCadaVolta()
    {
        ExecutionSession session = Fanuc(Laco);
        var breakpoints = new HashSet<int> { 4 };

        var stops = new List<double?>();
        while (!session.IsFinished)
        {
            session.Continue(breakpoints);
            if (session.NextLine == 4)
            {
                stops.Add(session.Variables["#1"]);
                Assert.Equal(3, session.LastBlock!.Line);  // a linha 4 ainda não rodou
            }
        }

        Assert.Equal([0, 1, 2], stops);
    }

    [Fact]
    public void RunToLineDeixaOProximoBlocoNaLinha()
    {
        ExecutionSession session = Fanuc(Laco);
        session.RunToLine(6);
        Assert.Equal(6, session.NextLine);
        Assert.Equal(3, session.Variables["#1"]);
        Assert.Equal(3, session.MoveCount);
        Assert.Equal(3, session.Position.X, 9);
    }

    [Fact]
    public void PilhaDeChamadasDaMacroG65ComArgumentosLocais()
    {
        ExecutionSession session = Fanuc("""
            O0001
            #1=99
            G65 P9010 A5. B2.
            G01 X#1 F100
            M30
            O9010
            G01 X#1 Y#2 F100
            M99
            """);

        session.RunToLine(7);
        IReadOnlyList<CallFrame> stack = session.CallStack;
        Assert.Equal(2, stack.Count);
        Assert.Equal("principal", stack[0].Program);
        Assert.Equal(4, stack[0].NextLine);           // volta para a linha 4 depois do M99
        Assert.Equal(5, session.Variables["#1"]);    // A → #1 local da macro
        Assert.Equal(2, session.Variables["#2"]);

        ExecutedBlock inside = session.Step()!;
        Assert.Equal(1, inside.Depth);
        Assert.Equal(7, inside.Line);

        session.RunToLine(4);
        Assert.Single(session.CallStack);
        Assert.Equal(99, session.Variables["#1"]);   // de volta às locais do principal
    }

    [Fact]
    public void RepeticaoDeSubprogramaAparecenaPilha()
    {
        ExecutionSession session = Fanuc("""
            O0001
            M98 P2000 L3
            M30
            O2000
            G91 G01 X10. F100
            G90
            M99
            """);

        session.RunToLine(5);
        Assert.Equal(3, session.CallStack[^1].RepeatsLeft);
        session.Continue(new HashSet<int> { 5 });
        Assert.Equal(2, session.CallStack[^1].RepeatsLeft);

        ToolPath path = session.Finish();
        Assert.Equal(30, path.Moves[^1].End.X, 9);
    }

    [Fact]
    public void ParametrosRDoSiemens()
    {
        ExecutionSession session = Siemens("""
            R1=0
            WHILE R1<2
            R1=R1+1
            G1 X=R1 F100
            ENDWHILE
            M30
            """);

        session.RunToLine(4);
        Assert.Equal(1, session.Variables["R1"]);
        ExecutedBlock move = session.Step()!;
        Assert.Equal(1, move.MoveCount);
    }

    [Fact]
    public void LacoInfinitoTerminaASessaoComErro()
    {
        ExecutionSession session = Fanuc("N10 #1=1\nN20 GOTO10", new MachineSettings { MaxExecutedBlocks = 50 });
        int steps = 0;
        while (session.Step() is not null)
        {
            steps++;
        }

        Assert.Equal(50, steps);
        Assert.True(session.IsFinished);
        Assert.Contains(session.Diagnostics, d => d.Message.Contains("laço infinito"));
    }

    [Fact]
    public void FinishDevolveSempreAMesmaTrajetoria()
    {
        ExecutionSession session = Fanuc(Laco);
        session.Step();
        ToolPath path = session.Finish();
        Assert.Same(path, session.Finish());
        Assert.True(session.IsFinished);
        Assert.Null(session.Step());
        Assert.Equal(3, path.Moves.Count);
    }

    /// <summary>Todos os exemplos: passo a passo (com registro de variáveis) e direto dão a mesma trajetória.</summary>
    [Fact]
    public void PassoAPassoEDiretoGeramAMesmaTrajetoriaEmTodosOsExemplos()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "samples");
        string[] files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
        Assert.True(files.Length >= 40);

        foreach (string file in files)
        {
            Dialect dialect = DialectDetector.FromFileName(file);
            string folder = Path.GetDirectoryName(file)!;
            var settings = new MachineSettings { LoadSubprogram = name => LoadSubprogram(folder, name) };
            string text = File.ReadAllText(file);

            ToolPath direct = MachineExecutor.Execute(GCodeParser.Parse(text, dialect), settings);

            ExecutionSession session = ExecutionSession.Start(GCodeParser.Parse(text, dialect), settings);
            while (session.Step() is not null)
            {
            }

            ToolPath stepped = session.Finish();
            Assert.True(direct.Moves.SequenceEqual(stepped.Moves), Path.GetFileName(file));
            Assert.Equal(direct.Diagnostics.Select(d => d.ToString()), stepped.Diagnostics.Select(d => d.ToString()));
        }
    }

    private static string? LoadSubprogram(string folder, string name)
    {
        foreach (string candidate in new[] { name, name + ".nc", "O" + name + ".nc", name + ".SPF", name + ".spf" })
        {
            string path = Path.Combine(folder, candidate);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return null;
    }
}
