using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Setup;

/// <summary>De onde veio o bruto usado na simulação e nos relatórios.</summary>
public enum StockSource
{
    /// <summary>Bruto informado no setup do programa.</summary>
    Setup,

    /// <summary>Lido do comentário do programa, ex.: "(BRUTO 100 X 80 X 20)".</summary>
    Comment,

    /// <summary>Nenhum informado: a simulação calcula pela trajetória.</summary>
    Automatic,
}

/// <summary>Bruto efetivo e a origem dele (Stock null quando automático).</summary>
public sealed record EffectiveStockResult(Stock? Stock, StockSource Source);

/// <summary>
/// Liga o setup ao resto do Core. É o único lugar que decide de onde vêm o bruto e o diâmetro
/// das ferramentas, para a web e o desktop chegarem sempre ao mesmo resultado.
/// </summary>
public static class SetupBinding
{
    /// <summary>
    /// Bruto do setup no formato da simulação. Fresa: prisma vira bloco; redondo e tubo viram
    /// o bloco que os envolve (a simulação 2,5D trabalha com bloco). Torno: redondo e tubo viram
    /// barra com a face no topo; prisma no torno não tem equivalente (null).
    /// </summary>
    public static Stock? ToStock(this SetupStock stock, MachineKind machine)
    {
        if (stock.Validate() is not null)
        {
            return null;
        }

        if (machine == MachineKind.Lathe)
        {
            return stock.IsRound ? Stock.Bar(stock.Diameter, stock.Height, stock.Top) : null;
        }

        (double minX, double minY, double maxX, double maxY) = stock.Footprint;
        return Stock.Block(minX, maxX, minY, maxY, stock.Z, stock.Top);
    }

    /// <summary>Bruto da simulação → bruto do setup (fresa: prisma; torno: redondo no eixo).</summary>
    public static SetupStock FromStock(Stock stock, string material = "Aço") => stock.Machine == MachineKind.Lathe
        ? new SetupStock
        {
            Kind = SetupStockKind.Round, X = 0, Y = 0, Z = stock.MinZ, Height = stock.Length, Diameter = stock.Diameter, Material = material,
        }
        : new SetupStock
        {
            Kind = SetupStockKind.Prism, X = stock.MinX, Y = stock.MinY, Z = stock.MinZ,
            Width = stock.MaxX - stock.MinX, Depth = stock.MaxY - stock.MinY, Height = stock.MaxZ - stock.MinZ, Material = material,
        };

    /// <summary>
    /// Bruto que vale para o programa. Precedência: setup salvo → comentário (BRUTO ...) → automático.
    /// Um bruto do setup que não serve para a máquina (prisma no torno) é ignorado.
    /// </summary>
    public static EffectiveStockResult EffectiveStock(MachiningSetup? setup, MachineKind machine, string programText, Bounds3 cutting)
    {
        if (setup?.Stock?.ToStock(machine) is { } fromSetup)
        {
            return new EffectiveStockResult(fromSetup, StockSource.Setup);
        }

        if (FromProgramComments(programText, machine, cutting) is { } fromComment)
        {
            return new EffectiveStockResult(fromComment, StockSource.Comment);
        }

        return new EffectiveStockResult(null, StockSource.Automatic);
    }

    /// <summary>Primeiro comentário do programa que descreve um bruto.</summary>
    public static Stock? FromProgramComments(string programText, MachineKind machine, Bounds3 cutting)
    {
        foreach (string line in (programText ?? "").Split('\n'))
        {
            if (Stock.FromComment(line, machine, cutting) is { } stock)
            {
                return stock;
            }
        }

        return null;
    }

    /// <summary>Bruto que envolve o corte com 2 mm de folga (topo em Z0 na fresa, face no maior Z no torno).</summary>
    public static Stock FitToPath(MachineKind machine, Bounds3 cutting)
    {
        Bounds3 b = cutting.IsEmpty ? Bounds3.Empty.Include(Point3.Origin) : cutting;
        const double margin = 2;
        return machine == MachineKind.Lathe
            ? Stock.Bar(Math.Round((Math.Abs(b.Max.X) + margin) * 2, 1), Math.Round(Math.Max(1, b.Max.Z - b.Min.Z + margin), 1), Math.Max(0, b.Max.Z))
            : Stock.Block(b.Min.X - margin, b.Max.X + margin, b.Min.Y - margin, b.Max.Y + margin, Math.Min(b.Min.Z, 0) - margin, 0);
    }

    /// <summary>Sugestão de bruto para quem vai informar um: do comentário, se houver; senão, pela trajetória.</summary>
    public static Stock SuggestStock(MachineKind machine, string programText, Bounds3 cutting) =>
        FromProgramComments(programText, machine, cutting) ?? FitToPath(machine, cutting);

    /// <summary>Perfil de corte da ferramenta do setup, para a remoção de material.</summary>
    public static ToolProfile ToProfile(this SetupTool tool) => tool.Kind switch
    {
        ToolKind.BallEndMill => new ToolProfile(ToolTipShape.Ball, tool.Radius),
        ToolKind.BullNoseEndMill => new ToolProfile(ToolTipShape.BullNose, tool.Radius, tool.CornerRadius),
        ToolKind.Drill => new ToolProfile(ToolTipShape.Cone, tool.Radius, TipAngle: tool.TipAngle),
        _ => new ToolProfile(ToolTipShape.Flat, tool.Radius),
    };

    /// <summary>
    /// Perfil da ferramenta T: o do setup, se ela estiver cadastrada nele; senão, topo reto com o
    /// diâmetro de <paramref name="fallbackDiameter"/> (tabela ou padrão da máquina).
    /// </summary>
    public static ToolProfile ProfileOf(MachiningSetup? setup, int tool, double fallbackDiameter) =>
        setup?.ToolOf(tool) is { } t && t.Validate() is null ? t.ToProfile() : ToolProfile.Flat(fallbackDiameter);

    /// <summary>
    /// Configuração do executor com as ferramentas do setup. Precedência do diâmetro (para G41/G42 e
    /// simulação): setup → tabela já presente em <paramref name="settings"/> → comentário da troca
    /// (este último aplicado pelo <see cref="ProgramAnalyzer"/>).
    /// </summary>
    public static MachineSettings ApplyTo(this MachiningSetup? setup, MachineSettings settings)
    {
        if (setup is null || setup.Tools.Count == 0)
        {
            return settings;
        }

        // Cópia: a análise roda em outra thread e não deve ver edições do setup no meio
        Dictionary<int, double> diameters = setup.Tools
            .Where(t => t.Diameter > 0)
            .GroupBy(t => t.Number)
            .ToDictionary(g => g.Key, g => g.Last().Diameter);
        Func<int, double>? table = settings.ToolDiameterOf;
        return settings with
        {
            ToolDiameterOf = tool => diameters.TryGetValue(tool, out double d) ? d : table?.Invoke(tool) ?? 0,
        };
    }
}
