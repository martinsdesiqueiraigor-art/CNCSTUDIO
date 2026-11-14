using System.Security.Cryptography;
using System.Text;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>Forma do bruto no pacote do CAM para o Code (CAM-13), na mesma convenção do bruto do setup do Code.</summary>
public enum HandoffStockKind { Prism, Round, Tube, Step }

/// <summary>
/// Bruto do pacote, já no referencial do programa (o mesmo zero que deslocou as operações). Mesma convenção do bruto do setup:
/// <list type="bullet">
/// <item>Prisma: (X, Y) é o canto de menor X e menor Y; Width × Depth; Z é o fundo e Height a altura.</item>
/// <item>Redondo e tubo em pé (eixo Z): (X, Y) é o centro, Z o fundo, Height o comprimento.</item>
/// <item>Deitado (eixo X ou Y): o campo do eixo é o começo do cilindro, Height o comprimento, os outros dois campos são o centro.</item>
/// <item>STEP: só o caminho e o resumo (caixa e volume nas coordenadas do STEP); não é convertido em bloco e não é desenhado no simulador.</item>
/// </list>
/// Sem material: o CAM não guarda material (nada suposto).
/// </summary>
public sealed record HandoffStock(HandoffStockKind Kind)
{
    public StockAxis Axis { get; init; } = StockAxis.Z;

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }

    public double Width { get; init; }

    public double Depth { get; init; }

    public double Height { get; init; }

    public double Diameter { get; init; }

    public double InnerDiameter { get; init; }

    /// <summary>STEP: caminho do arquivo como foi escolhido no CAM.</summary>
    public string? StepPath { get; init; }

    /// <summary>STEP: caixa e volume lidos na importação (coordenadas do STEP).</summary>
    public StepStockSummary? StepSummary { get; init; }

    /// <summary>STEP: o simulador não desenha este bruto (só avisa, decisão do Igor de 08/11, opção a).</summary>
    public bool NotDrawn => Kind == HandoffStockKind.Step;

    /// <summary>
    /// O bruto do trabalho no referencial do programa: o sólido de <see cref="CamJobStock.Check"/> (referencial interno) menos a posição
    /// do zero (<see cref="CamJobZero.Position"/>), a mesma conta que desloca as operações. Arquivo antigo ("como antes") e bloco
    /// absoluto: posição (0, 0, 0), o bruto de hoje. Null se o bruto não está definido ou está incompleto, ou se o zero está incompleto.
    /// </summary>
    public static HandoffStock? InProgram(CamJob job)
    {
        if (CamJobStock.Check(job).Solid is not { } s || CamJobZero.Position(job) is not { } zero)
        {
            return null;
        }

        if (s.Kind == StockSolidKind.Step)
        {
            // STEP: não converte em bloco; leva caminho e resumo como estão no trabalho
            return new HandoffStock(HandoffStockKind.Step) { StepPath = job.StockShape!.StepPath, StepSummary = job.StockShape.StepSummary };
        }

        double minX = s.MinX - zero.X, minY = s.MinY - zero.Y, minZ = s.MinZ - zero.Z;
        if (s.Kind == StockSolidKind.Box)
        {
            return new HandoffStock(HandoffStockKind.Prism) { X = minX, Y = minY, Z = minZ, Width = s.SizeX, Depth = s.SizeY, Height = s.SizeZ };
        }

        double cx = minX + s.SizeX / 2, cy = minY + s.SizeY / 2, cz = minZ + s.SizeZ / 2;
        (double x, double y, double z) = s.Axis switch
        {
            StockAxis.X => (minX, cy, cz),
            StockAxis.Y => (cx, minY, cz),
            _ => (cx, cy, minZ),
        };
        return new HandoffStock(s.Kind == StockSolidKind.Tube ? HandoffStockKind.Tube : HandoffStockKind.Round)
        {
            Axis = s.Axis, X = x, Y = y, Z = z, Height = s.Length, Diameter = s.Diameter, InnerDiameter = s.Kind == StockSolidKind.Tube ? s.InnerDiameter : 0,
        };
    }

    /// <summary>Caixa que envolve o bruto no referencial do programa (STEP: null, não é convertido).</summary>
    public Bounds3? Extent()
    {
        double r = Diameter / 2;
        (double x0, double x1, double y0, double y1, double z0, double z1)? b = Kind switch
        {
            HandoffStockKind.Step => null,
            HandoffStockKind.Prism => (X, X + Width, Y, Y + Depth, Z, Z + Height),
            _ when Axis == StockAxis.X => (X, X + Height, Y - r, Y + r, Z - r, Z + r),
            _ when Axis == StockAxis.Y => (X - r, X + r, Y, Y + Height, Z - r, Z + r),
            _ => (X - r, X + r, Y - r, Y + r, Z, Z + Height),
        };
        return b is { } c ? Bounds3.Empty.Include(new Point3(c.x0, c.y0, c.z0)).Include(new Point3(c.x1, c.y1, c.z1)) : null;
    }
}

/// <summary>
/// Ferramenta no pacote. T, Ø, nome, rpm e avanços vêm sempre do trabalho (<see cref="CamTool"/>, a cópia dele). Tipo, raio de canto,
/// ângulo da ponta, comprimento de corte e suporte não existem no CAM: vêm do banco ativo quando o T está nele sem conflito (UX-04h C,
/// <see cref="CamHandoff.FromBank"/>), só os cadastrados; o resto fica null e vai para o JSON como null (nada suposto; o tipo da broca
/// na furação também não é deduzido da operação).
/// </summary>
public sealed record HandoffTool(int Number, double Diameter, string Name, double SpindleRpm, double FeedXY, double FeedZ)
{
    public string? Type { get; init; }

    public double? CornerRadius { get; init; }

    public double? TipAngle { get; init; }

    public double? CutLength { get; init; }

    public string? Holder { get; init; }
}

/// <summary>Zero da peça no pacote: ponto e Z0 do <see cref="JobZero"/>, se é de arquivo antigo, e o resumo do cartão.</summary>
public sealed record HandoffZero(ZeroPoint Point, ZeroLevel Level, bool AsBefore, string Summary);

/// <summary>
/// Pacote do CAM para o Code (CAM-13, versão 1): o que o programa gerado não diz (bruto, zero, fixação, ferramentas), guardado ao lado
/// do programa em <c>NOME.nc.casillas-cam</c>. Nada vai para o texto do G-code. O hash amarra o pacote ao programa: programa editado
/// ou de outro trabalho não confere. Sem material (o CAM não guarda).
/// </summary>
public sealed record CamHandoff(string ProgramHash, CamDialect Controller)
{
    public const int CurrentVersion = 1;

    /// <summary>Somado ao nome inteiro do programa ("PECA.nc" → "PECA.nc.casillas-cam"): PECA.nc e PECA.mpf não se misturam.</summary>
    public const string Extension = ".casillas-cam";

    public const string Mill = "fresa";

    public const string Lathe = "torno";

    /// <summary>Aviso quando o programa aberto não é o que o CAM gerou (editado ou outro programa).</summary>
    public const string MismatchWarning =
        "Os dados do CAM são de um programa diferente deste: o texto foi editado ou o arquivo é de outro programa. Bruto, ferramentas e zero seguem como vieram do CAM.";

    public int Version { get; init; } = CurrentVersion;

    /// <summary>"fresa" ou "torno" (só a fresa é preenchida por enquanto; o campo fica para o torno).</summary>
    public string Machine { get; init; } = Mill;

    /// <summary>Nome do perfil de máquina usado ao gerar (null = sem perfil).</summary>
    public string? MachineProfile { get; init; }

    public int? ProgramNumber { get; init; }

    public int? WorkOffset { get; init; }

    public HandoffZero? Zero { get; init; }

    /// <summary>Fixação em texto (tipo e descrição); não é desenhada.</summary>
    public JobFixture? Fixture { get; init; }

    public HandoffStock? Stock { get; init; }

    public IReadOnlyList<HandoffTool> Tools { get; init; } = [];

    /// <summary>Nome do arquivo do pacote ao lado do programa.</summary>
    public static string FileNameFor(string programFileName) => programFileName + Extension;

    /// <summary>
    /// Monta o pacote do trabalho de fresa para o programa gerado (o texto do programa não é mudado). <paramref name="bank"/> é o banco
    /// ativo da fresa (<see cref="ToolBankPick.ActiveMillBank"/>), já lido pela casca; null = sem banco, o pacote de antes.
    /// </summary>
    public static CamHandoff Create(CamJob job, string program, MachineProfile? machine, ToolBank? bank = null) => new(Hash(program), job.Dialect)
    {
        MachineProfile = machine?.Name,
        ProgramNumber = job.ProgramNumber,
        WorkOffset = job.WorkOffset,
        Zero = job.Zero is { Point: { } p, Level: { } l } z ? new HandoffZero(p, l, z.AsBefore, CamJobZero.Summary(job)) : null,
        Fixture = job.Fixture,
        Stock = HandoffStock.InProgram(job),
        Tools = ProgramTools(job).Select(t => FromBank(t, bank)).ToList(),
    };

    /// <summary>
    /// Ferramenta do pacote: a cópia do trabalho e, se o T está no banco sem conflito (Ø e nome, a mesma comparação do aviso do B2,
    /// <see cref="ToolBankPick.SameTool"/>), os cinco campos que o banco tem cadastrados. Inativa no banco preenche igual. T fora do
    /// banco ou em conflito: os cinco ficam null.
    /// </summary>
    public static HandoffTool FromBank(CamTool t, ToolBank? bank)
    {
        var tool = new HandoffTool(t.Number, t.Diameter, t.Name, t.SpindleRpm, t.FeedXY, t.FeedZ);
        if (bank?.Tools.FirstOrDefault(b => b.Number == t.Number) is not { } b || !ToolBankPick.SameTool(t, b))
        {
            return tool;
        }

        return tool with
        {
            Type = b.Type is { } type ? BankToolTypes.Code(type) : null,
            CornerRadius = b.CornerRadius,
            TipAngle = b.TipAngle,
            CutLength = b.CuttingLength,
            Holder = HolderText(b.HolderDiameter, b.HolderLength),
        };
    }

    /// <summary>
    /// Suporte em texto (o campo do pacote é texto e o banco guarda Ø e comprimento): "Ø 35 × 45 mm", "Ø 35 mm" ou "comprimento 45 mm";
    /// null se o banco não tem nenhum dos dois.
    /// </summary>
    public static string? HolderText(double? diameter, double? length)
    {
        static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        return (diameter, length) switch
        {
            ({ } d, { } l) => $"Ø {F(d)} × {F(l)} mm",
            ({ } d, null) => $"Ø {F(d)} mm",
            (null, { } l) => $"comprimento {F(l)} mm",
            _ => null,
        };
    }

    /// <summary>
    /// Ferramentas das operações ligadas (as que entram no programa gerado; decisão do Igor de 08/11), agrupadas como em
    /// <see cref="CamJobInfo.ToolsUsed"/> (T e Ø, a primeira de cada grupo, em ordem de T). O <c>ToolsUsed</c> segue igual.
    /// </summary>
    private static IEnumerable<CamTool> ProgramTools(CamJob job) =>
        job.Operations.Where(o => o.Enabled).Select(o => o.Tool).GroupBy(t => (t.Number, t.Diameter)).Select(g => g.First()).OrderBy(t => t.Number);

    /// <summary>SHA-256 (hex minúsculo) do texto do programa com as quebras de linha normalizadas (CRLF, CR e LF dão o mesmo hash).</summary>
    public static string Hash(string program)
    {
        string text = program.Replace("\r\n", "\n").Replace('\r', '\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>O programa é o que gerou este pacote?</summary>
    public bool Matches(string program) => Hash(program) == ProgramHash;
}
