using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Cam;

/// <summary>Aviso do pacote do CAM no Code: o tipo (para a tela saber onde mostrar) e o texto. Nenhum bloqueia a simulação.</summary>
public sealed record HandoffNotice(HandoffNoticeKind Kind, string Text);

/// <summary>Os 8 avisos do CAM-13 PR 2, na ordem do handoff.</summary>
public enum HandoffNoticeKind { ProgramChanged, StockDiffers, ToolDiameter, WorkOffset, StepStock, NoStock, FutureVersion, Unreadable }

/// <summary>
/// Regras de "quem vale" quando o Code recebe o pacote do CAM (CAM-13 PR 2, decisões do Igor de 08/11). O texto do G-code nunca muda,
/// o setup não sobe de versão e a tabela geral de ferramentas e a de zeros não são alteradas.
/// <list type="bullet">
/// <item>Bruto: o do CAM só preenche o setup vazio; setup já informado e diferente: aviso, nada sobrescrito.</item>
/// <item>Ferramentas: o Ø do CAM vale neste programa (depois do setup do programa, antes da tabela geral); conflito com a tabela: aviso.</item>
/// <item>Tabela de zeros: G5x do programa diferente de zero só gera aviso.</item>
/// </list>
/// </summary>
public static class CamHandoffCode
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Aviso 1 (fixo): o mesmo texto de <see cref="CamHandoff.MismatchWarning"/>.</summary>
    public const string ProgramChangedText = CamHandoff.MismatchWarning;

    public const string NoStockText = "O CAM não informou o bruto (não definido ou incompleto). O simulador usa o bruto automático.";

    private static string N(double v) => v.ToString("0.###", Br);

    /// <summary>Nome do programa enviado ao Code: Siemens com .mpf (o Code abre como Siemens), Fanuc com .nc.</summary>
    public static string ProgramFileName(string baseName, CamDialect dialect) => baseName + (dialect == CamDialect.Siemens ? ".mpf" : ".nc");

    /// <summary>Dialeto do Code para o controle do pacote.</summary>
    public static Dialect DialectOf(CamHandoff p) => p.Controller == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc;

    /// <summary>
    /// Bruto do pacote como bruto do setup (mesma convenção, campo a campo). STEP ou sem bruto: null (o simulador usa o automático).
    /// Material vazio: o CAM não informa material e nada é suposto (o "Aço" do setup novo não entra).
    /// </summary>
    public static SetupStock? ToSetupStock(HandoffStock? s) => s switch
    {
        null or { Kind: HandoffStockKind.Step } => null,
        { Kind: HandoffStockKind.Prism } => new SetupStock
        {
            Kind = SetupStockKind.Prism, X = s.X, Y = s.Y, Z = s.Z, Width = s.Width, Depth = s.Depth, Height = s.Height, Material = "",
        },
        _ => new SetupStock
        {
            Kind = s.Kind == HandoffStockKind.Tube ? SetupStockKind.Tube : SetupStockKind.Round,
            Axis = s.Axis switch { StockAxis.X => SetupAxis.X, StockAxis.Y => SetupAxis.Y, _ => SetupAxis.Z },
            X = s.X, Y = s.Y, Z = s.Z, Height = s.Height, Diameter = s.Diameter,
            // o campo existe no setup com padrão 40; no redondo não é usado, então fica o do setup novo
            InnerDiameter = s.Kind == HandoffStockKind.Tube ? s.InnerDiameter : new SetupStock().InnerDiameter,
            Material = "",
        },
    };

    /// <summary>Mesmo bruto (forma, eixo, posição e medidas que a forma usa; o material não conta).</summary>
    public static bool SameStock(SetupStock a, SetupStock b)
    {
        static bool Eq(double x, double y) => Math.Abs(x - y) < 1e-6;
        if (a.Kind != b.Kind || !Eq(a.X, b.X) || !Eq(a.Y, b.Y) || !Eq(a.Z, b.Z) || !Eq(a.Height, b.Height))
        {
            return false;
        }

        return a.Kind == SetupStockKind.Prism
            ? Eq(a.Width, b.Width) && Eq(a.Depth, b.Depth)
            : a.Axis == b.Axis && Eq(a.Diameter, b.Diameter) && (a.Kind != SetupStockKind.Tube || Eq(a.InnerDiameter, b.InnerDiameter));
    }

    /// <summary>Descrição curta do bruto para o aviso: "prisma 90 × 70 × 26 mm, canto X −45 Y −35 Z −5".</summary>
    public static string Describe(SetupStock s)
    {
        string where = $"X {N(s.X)} Y {N(s.Y)} Z {N(s.Z)}";
        string axis = s.Axis == SetupAxis.Z ? "" : $", eixo {s.Axis}";
        return s.Kind switch
        {
            SetupStockKind.Prism => $"prisma {N(s.Width)} × {N(s.Depth)} × {N(s.Height)} mm, canto {where}",
            SetupStockKind.Tube => $"tubo Ø{N(s.Diameter)}/Ø{N(s.InnerDiameter)} × {N(s.Height)} mm{axis}, em {where}",
            _ => $"redondo Ø{N(s.Diameter)} × {N(s.Height)} mm{axis}, em {where}",
        };
    }

    /// <summary>
    /// Preenche o bruto do setup com o do CAM só quando não há bruto informado: nem setup salvo com bruto, nem comentário (BRUTO ...) no
    /// texto (decisão do Igor de 08/11: setup → comentário → CAM → automático). Devolve se preencheu. Nunca sobrescreve.
    /// </summary>
    public static bool FillStock(MachiningSetup setup, CamHandoff? p, string programText)
    {
        if (setup.Stock is not null || CommentStock(programText) is not null || ToSetupStock(p?.Stock) is not { } cam)
        {
            return false;
        }

        setup.Stock = cam;
        return true;
    }

    /// <summary>Bruto do comentário (BRUTO ...) do texto, como o simulador da fresa o lê; null se não há.</summary>
    private static Stock? CommentStock(string programText) => SetupBinding.FromProgramComments(programText, MachineKind.Mill, Bounds3.Empty);

    /// <summary>
    /// O comentário descreve o mesmo bruto do CAM? O comentário só diz as medidas (a posição é um palpite do simulador), então compara
    /// só as medidas de um prisma; redondo e tubo do CAM nunca batem com o bloco do comentário.
    /// </summary>
    private static bool SameAsComment(SetupStock cam, Stock comment)
    {
        static bool Eq(double x, double y) => Math.Abs(x - y) < 1e-6;
        return cam.Kind == SetupStockKind.Prism
            && Eq(cam.Width, comment.MaxX - comment.MinX) && Eq(cam.Depth, comment.MaxY - comment.MinY) && Eq(cam.Height, comment.MaxZ - comment.MinZ);
    }

    /// <summary>O bruto do setup é o do CAM (etiqueta "do CAM")? Editar o bruto tira a etiqueta.</summary>
    public static bool StockFromCam(MachiningSetup setup, CamHandoff? p) =>
        setup.Stock is { } s && ToSetupStock(p?.Stock) is { } cam && SameStock(s, cam);

    /// <summary>Ø do CAM por T, para este programa (a tabela geral não muda).</summary>
    public static IReadOnlyDictionary<int, double> Diameters(CamHandoff? p) =>
        (p?.Tools ?? []).Where(t => t.Diameter > 0).GroupBy(t => t.Number).ToDictionary(g => g.Key, g => g.First().Diameter);

    /// <summary>
    /// Ø do CAM antes da tabela geral. Chamar antes de <see cref="SetupBinding.ApplyTo"/>: a ordem fica setup do programa → CAM → tabela
    /// → comentário → padrão.
    /// </summary>
    public static MachineSettings ApplyDiameters(MachineSettings settings, CamHandoff? p)
    {
        IReadOnlyDictionary<int, double> cam = Diameters(p);
        if (cam.Count == 0)
        {
            return settings;
        }

        Func<int, double>? table = settings.ToolDiameterOf;
        return settings with { ToolDiameterOf = tool => cam.TryGetValue(tool, out double d) ? d : table?.Invoke(tool) ?? 0 };
    }

    /// <summary>
    /// Ø por T do banco ativo da fresa, para o aviso 3 (UX-04h C: compara com o banco ativo, não com a tabela antiga). 0 = T fora do
    /// banco, sem Ø, ou sem banco (nada a avisar). Inativa conta.
    /// </summary>
    public static Func<int, double> BankDiameterOf(ToolBank? activeMillBank)
    {
        Dictionary<int, double> d = (activeMillBank?.Tools ?? []).Where(t => t.Diameter is not null)
            .GroupBy(t => t.Number).ToDictionary(g => g.Key, g => g.First().Diameter!.Value);
        return t => d.TryGetValue(t, out double v) ? v : 0;
    }

    /// <summary>
    /// Avisos que valem agora (recalcular a cada mudança do texto, do setup ou do banco). <paramref name="readWarning"/> é o aviso da
    /// leitura do arquivo ao lado (versão nova ou ilegível); <paramref name="tableDiameterOf"/> é o Ø do banco ativo da fresa
    /// (<see cref="BankDiameterOf"/>) e devolve 0 para T fora dele.
    /// </summary>
    public static IReadOnlyList<HandoffNotice> Notices(CamHandoff? p, string? readWarning, MachiningSetup? setup, string programText,
        Func<int, double> tableDiameterOf, IReadOnlyDictionary<int, Point3> workOffsets)
    {
        var list = new List<HandoffNotice>();
        if (readWarning is not null)
        {
            HandoffNoticeKind kind = readWarning == CamHandoffJson.FutureWarning ? HandoffNoticeKind.FutureVersion : HandoffNoticeKind.Unreadable;
            list.Add(new HandoffNotice(kind, readWarning));
        }

        if (p is null)
        {
            return list;
        }

        if (!p.Matches(programText))
        {
            list.Add(new HandoffNotice(HandoffNoticeKind.ProgramChanged, ProgramChangedText));
        }

        if (p.Stock is { Kind: HandoffStockKind.Step } step)
        {
            list.Add(new HandoffNotice(HandoffNoticeKind.StepStock, StepText(step)));
        }
        else if (ToSetupStock(p.Stock) is not { } cam)
        {
            list.Add(new HandoffNotice(HandoffNoticeKind.NoStock, NoStockText));
        }
        else if (setup?.Stock is { } informed ? !SameStock(informed, cam) : CommentStock(programText) is { } comment && !SameAsComment(cam, comment))
        {
            list.Add(new HandoffNotice(HandoffNoticeKind.StockDiffers,
                $"Este programa já tem um bruto informado no Code. O bruto do CAM ({Describe(cam)}) é diferente e não foi aplicado."));
        }

        foreach ((int t, double d) in Diameters(p).OrderBy(kv => kv.Key))
        {
            double table = tableDiameterOf(t);
            if (table > 0 && Math.Abs(table - d) > 1e-6)
            {
                list.Add(new HandoffNotice(HandoffNoticeKind.ToolDiameter,
                    $"T{t}: o CAM usou Ø {N(d)} mm e o banco tem Ø {N(table)} mm. Neste programa vale o do CAM; o banco não foi alterado."));
            }
        }

        if (p.WorkOffset is { } g && workOffsets.TryGetValue(g, out Point3 o) && (o.X != 0 || o.Y != 0 || o.Z != 0))
        {
            list.Add(new HandoffNotice(HandoffNoticeKind.WorkOffset,
                $"A tabela de zeros da máquina tem G{g} diferente de zero ({N(o.X)}, {N(o.Y)}, {N(o.Z)}). A trajetória fica deslocada em relação ao bruto."));
        }

        return list;
    }

    private static string StepText(HandoffStock s)
    {
        string name = s.StepPath is { } path ? CamJobStock.FileNameOf(path) : "sem nome";
        string size = s.StepSummary is { } b
            ? $"{N(b.SizeX)} × {N(b.SizeY)} × {N(b.SizeZ)} mm, {(b.Volume / 1000).ToString("0.##", Br)} cm³"
            : "sem resumo";
        return $"O bruto do CAM é um STEP ({name}, {size}). O simulador não desenha bruto em STEP.";
    }
}

/// <summary>
/// O pacote no disco, ao lado do programa: <c>PECA.nc.casillas-cam</c> (o nome inteiro do programa mais a extensão). Só nasce quando o
/// programa é salvo no Code; "Enviar ao Code" mantém o pacote na memória da aba. Um Code antigo ignora o arquivo.
/// </summary>
public static class CamHandoffFile
{
    public static string PathFor(string programPath) => CamHandoff.FileNameFor(programPath);

    /// <summary>Lê o pacote ao lado do programa. Sem arquivo: (null, null), o programa abre como hoje. Nunca lança exceção.</summary>
    public static HandoffReadResult Read(string programPath)
    {
        string path = PathFor(programPath);
        try
        {
            return File.Exists(path) ? CamHandoffJson.Parse(File.ReadAllText(path)) : new HandoffReadResult(null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HandoffReadResult(null, CamHandoffJson.InvalidWarning);
        }
    }

    /// <summary>Grava o pacote ao lado do programa (o hash continua o do programa que o CAM gerou).</summary>
    public static void Write(string programPath, CamHandoff p) => File.WriteAllText(PathFor(programPath), CamHandoffJson.ToJson(p));
}
