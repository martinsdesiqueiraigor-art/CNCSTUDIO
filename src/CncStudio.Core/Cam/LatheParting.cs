namespace CncStudio.Core.Cam;

/// <summary>
/// Sangramento (corte da peça), TORNO-T3: G1 radial no <see cref="CutZ"/> (Z da aresta de maior Z do bedame), do
/// <see cref="StartDiameter"/> até o <see cref="FinalDiameter"/> (0 = até o centro, a peça se solta; maior que 0 deixa um pino).
/// <see cref="ReducedFeed"/> vale do <see cref="ReducedFromDiameter"/> para baixo. Mergulho e recuo quebram o cavaco.
/// Tudo em mm; avanços em mm/rot.
/// </summary>
public sealed record PartingParams(
    double CutZ, double StartDiameter, double FinalDiameter, double ToolWidth, double Feed, double ReducedFeed, double ReducedFromDiameter,
    double Peck, double Retract, double Clearance = 2);

/// <summary>Percurso do sangramento em movimentos simples (G0/G1, igual no Fanuc e no Siemens). G-code novo: nunca rodado em máquina.</summary>
public static class LatheParting
{
    /// <summary>Aviso fixo de toda operação de sangramento.</summary>
    public const string Warning = "Sangramento: G-code novo, nunca rodado em máquina. Simule e rode a primeira vez no ar.";

    /// <summary>Aviso fixo quando o corte vai até o centro (Ø final 0).</summary>
    public const string LoosePartWarning =
        "Sangramento até o centro: a peça se solta e cai. O app não verifica fixação nem a queda da peça; o simulador não mostra a queda.";

    /// <summary>Motivo do bloqueio de operação depois do sangramento.</summary>
    public const string LastOperation = "O sangramento é a última operação da fixação: não dá para ter operação depois dele.";

    public static LatheResult Build(LatheTool tool, double safeZ, double safeR, PartingParams p)
    {
        static LatheResult Fail(string message) => new(null, message);
        if (tool.Validate() is { } toolError)
        {
            return Fail(toolError);
        }

        if (p.StartDiameter <= 0 || p.ToolWidth <= 0 || p.Feed <= 0 || p.ReducedFeed <= 0 || p.Peck <= 0 || p.Retract <= 0 || p.Clearance <= 0)
        {
            return Fail("Ø de início, largura, avanços, mergulho, recuo e folga precisam ser maiores que zero.");
        }

        if (p.FinalDiameter < 0)
        {
            return Fail("O Ø final não pode ser negativo (0 = até o centro).");
        }

        if (p.FinalDiameter >= p.StartDiameter)
        {
            return Fail("O Ø final precisa ser menor que o Ø de início.");
        }

        if (p.ReducedFromDiameter <= p.FinalDiameter)
        {
            return Fail("O Ø onde começa o avanço reduzido precisa ser maior que o Ø final (o fim do corte é sempre com avanço reduzido).");
        }

        double rStart = p.StartDiameter / 2, rFinal = p.FinalDiameter / 2, rReduced = p.ReducedFromDiameter / 2;
        double clear = rStart + p.Clearance;
        var moves = new List<LatheMove>();
        void Rapid(double z, double r) => moves.Add(new LatheMove(MoveKind.Rapid, z, r));

        // corta até o raio pedido, trocando para o avanço reduzido ao cruzar o Ø digitado
        void CutTo(double from, double to)
        {
            if (from > rReduced + 1e-9 && to < rReduced - 1e-9)
            {
                moves.Add(new LatheMove(MoveKind.Feed, p.CutZ, rReduced, p.Feed));
                moves.Add(new LatheMove(MoveKind.Feed, p.CutZ, to, p.ReducedFeed));
            }
            else
            {
                moves.Add(new LatheMove(MoveKind.Feed, p.CutZ, to, to < rReduced - 1e-9 ? p.ReducedFeed : p.Feed));
            }
        }

        Rapid(p.CutZ, safeR);
        Rapid(p.CutZ, clear);
        double depthTotal = rStart - rFinal;
        double done = 0, position = clear;
        while (done < depthTotal - 1e-9)
        {
            double previous = done;
            done = Math.Min(depthTotal, done + p.Peck);
            if (previous > 0)
            {
                position = rStart - Math.Max(0, previous - 0.2);       // volta perto do fundo anterior
                Rapid(p.CutZ, position);
            }

            CutTo(position, rStart - done);
            if (done < depthTotal - 1e-9)
            {
                Rapid(p.CutZ, rStart - Math.Max(0, done - p.Retract));  // quebra-cavaco
            }
        }

        Rapid(p.CutZ, clear);
        Rapid(p.CutZ, safeR);
        Rapid(safeZ, safeR);
        List<string> warnings = [Warning];
        if (p.FinalDiameter == 0)
        {
            warnings.Add(LoosePartWarning);
        }

        return new LatheResult(new LatheOperation("Sangramento", tool, safeZ, safeR, moves, null, warnings), null);
    }

    /// <summary>
    /// Índice da primeira operação que vem depois de um sangramento (ligadas ou não, para a tela travar a ordem); null = ordem boa.
    /// </summary>
    public static int? FirstAfterParting(IReadOnlyList<LatheOperationSpec> operations)
    {
        int parting = operations.ToList().FindIndex(o => o.Kind == LatheOpKind.Parting);
        return parting >= 0 && parting < operations.Count - 1 ? parting + 1 : null;
    }
}
