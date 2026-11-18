namespace CncStudio.Core.Cam;

/// <summary>
/// Canal de face (axial), TORNO-T2. O canal ocupa a faixa de Ø de <see cref="InnerDiameter"/> a <see cref="OuterDiameter"/> e vai do
/// <see cref="FaceZ"/> ao <see cref="BottomZ"/> (menor). A ferramenta (bedame de face) tem a largura <see cref="ToolWidth"/>, medida
/// no raio, e o X do programa é a aresta de MAIOR Ø dela. <see cref="ReducedFeed"/> vale nos mergulhos em que o Ø programado é menor
/// ou igual a <see cref="ReducedFromDiameter"/> (0 = em nenhum). <see cref="ToolMinDiameter"/> e <see cref="ToolMaxDiameter"/>: faixa
/// de Ø do primeiro mergulho que o bedame de face aceita (a lâmina é curva), digitada. Tudo em mm; avanços em mm/rot.
/// </summary>
public sealed record FaceGroovingParams(
    double OuterDiameter, double InnerDiameter, double FaceZ, double BottomZ, double ToolWidth, double Peck, double Retract,
    double Feed, double ReducedFeed, double ReducedFromDiameter, double Allowance, double ToolMinDiameter, double ToolMaxDiameter,
    double Clearance = 2);

/// <summary>Percurso do canal de face em movimentos simples (G0/G1, igual no Fanuc e no Siemens). G-code novo: nunca rodado em máquina.</summary>
public static class LatheFaceGrooving
{
    /// <summary>Aviso fixo de toda operação de canal de face.</summary>
    public const string Warning =
        "Canal de face: G-code novo, nunca rodado em máquina. X do programa = aresta de maior Ø do bedame. Simule e rode a primeira vez no ar.";

    /// <summary>
    /// Desbaste em mergulhos em Z com quebra-cavaco, do Ø externo para o interno (passo de 80% da largura da ferramenta), e acabamento
    /// (com sobremetal maior que zero) das duas paredes e do fundo. O sobremetal fica nas paredes e no fundo durante o desbaste.
    /// </summary>
    public static LatheResult Build(LatheTool tool, double safeZ, double safeR, FaceGroovingParams p)
    {
        static LatheResult Fail(string message) => new(null, message);
        if (tool.Validate() is { } toolError)
        {
            return Fail(toolError);
        }

        if (p.OuterDiameter <= 0 || p.InnerDiameter <= 0 || p.ToolWidth <= 0 || p.Peck <= 0 || p.Retract <= 0 || p.Feed <= 0 || p.ReducedFeed <= 0 || p.Clearance <= 0)
        {
            return Fail("Diâmetros, largura, mergulho, recuo, avanços e folga precisam ser maiores que zero.");
        }

        if (p.Allowance < 0 || p.ReducedFromDiameter < 0)
        {
            return Fail("O sobremetal e o Ø do avanço reduzido não podem ser negativos.");
        }

        if (p.InnerDiameter >= p.OuterDiameter)
        {
            return Fail("O Ø externo do canal precisa ser maior que o Ø interno.");
        }

        if (p.BottomZ >= p.FaceZ)
        {
            return Fail("O Z do fundo precisa ser menor que o Z da face.");
        }

        double rOut = p.OuterDiameter / 2, rIn = p.InnerDiameter / 2;
        if (rOut - rIn < p.ToolWidth - 1e-6)
        {
            return Fail($"A ferramenta ({p.ToolWidth:0.##} mm) é mais larga que o canal ({rOut - rIn:0.##} mm no raio).");
        }

        if (p.ToolMinDiameter <= 0 || p.ToolMaxDiameter <= p.ToolMinDiameter)
        {
            return Fail("Informe a faixa de Ø do bedame de face: o Ø máximo precisa ser maior que o mínimo, e os dois maiores que zero.");
        }

        if (p.OuterDiameter < p.ToolMinDiameter - 1e-6 || p.OuterDiameter > p.ToolMaxDiameter + 1e-6)
        {
            return Fail($"O Ø externo do canal ({p.OuterDiameter:0.##}) está fora da faixa do bedame de face (Ø {p.ToolMinDiameter:0.##} a {p.ToolMaxDiameter:0.##}): a lâmina bateria na parede.");
        }

        double rInnerTool = rIn + p.ToolWidth;                 // X da ferramenta com a aresta de dentro na parede interna
        double span = rOut - rInnerTool;                       // curso útil no raio (0 = largura exata)
        bool finish = p.Allowance > 0 && span > 1e-6;
        double wall = finish ? Math.Min(p.Allowance, span / 2) : 0;
        double zRough = p.BottomZ + (finish ? p.Allowance : 0);
        if (zRough > p.FaceZ)
        {
            zRough = p.FaceZ;
        }

        double zClear = p.FaceZ + p.Clearance;
        var moves = new List<LatheMove>();
        void Rapid(double z, double r) => moves.Add(new LatheMove(MoveKind.Rapid, z, r));
        void Cut(double z, double r, double feed) => moves.Add(new LatheMove(MoveKind.Feed, z, r, feed));
        double FeedAt(double r) => p.ReducedFromDiameter > 0 && 2 * r <= p.ReducedFromDiameter + 1e-9 ? p.ReducedFeed : p.Feed;

        double first = rOut - wall, last = rInnerTool + wall;
        var positions = new List<double> { first };
        double step = Math.Max(0.1, p.ToolWidth * 0.8);
        for (double r = first - step; r > last + 1e-6; r -= step)
        {
            positions.Add(r);
        }

        if (last < first - 1e-6)
        {
            positions.Add(last);
        }

        Rapid(safeZ, first);
        double depthTotal = p.FaceZ - zRough;
        foreach (double r in positions)
        {
            Rapid(zClear, r);
            double feed = FeedAt(r);
            double done = 0;
            while (done < depthTotal - 1e-9)
            {
                double previous = done;
                done = Math.Min(depthTotal, done + p.Peck);
                if (previous > 0)
                {
                    Rapid(p.FaceZ - Math.Max(0, previous - 0.2), r);   // volta perto do fundo anterior
                }

                Cut(p.FaceZ - done, r, feed);
                if (done < depthTotal - 1e-9)
                {
                    Rapid(p.FaceZ - Math.Max(0, done - p.Retract), r);  // quebra-cavaco
                }
            }

            Rapid(zClear, r);
        }

        if (finish)
        {
            // 1) parede interna: mergulha e se afasta para o lado já limpo
            Rapid(zClear, rInnerTool);
            Cut(p.BottomZ, rInnerTool, FeedAt(rInnerTool));
            Cut(p.BottomZ, rInnerTool + wall, FeedAt(rInnerTool));
            Rapid(zClear, rInnerTool + wall);
            // 2) parede externa: mergulha e arrasta pelo fundo até a parede interna
            Rapid(zClear, rOut);
            Cut(p.BottomZ, rOut, FeedAt(rOut));
            Cut(p.BottomZ, rInnerTool, FeedAt(rInnerTool));
            Rapid(zClear, rInnerTool);
        }

        Rapid(safeZ, moves[^1].R);
        Rapid(safeZ, safeR);
        return new LatheResult(new LatheOperation("Canal de face", tool, safeZ, safeR, moves, null, [Warning]), null);
    }
}
