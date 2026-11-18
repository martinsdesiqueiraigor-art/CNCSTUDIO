using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>Onde fica o Z0 do programa do torno (TORNO-T5). X0 é sempre o centro da peça.</summary>
public enum LatheZeroFace
{
    /// <summary>Face pronta: o início do perfil (maior Z, lado de fora da placa).</summary>
    Finished,

    /// <summary>Face oposta: o fim do perfil (menor Z, lado da placa).</summary>
    Opposite,
}

/// <summary>
/// Z0 por trabalho (TORNO-T5). Tudo o que o usuário digita (perfil, Z das operações, Z seguro) continua no referencial do desenho
/// do perfil; só o programa sai deslocado, para o Z0 cair na face escolhida. Sem escolha (null, arquivo antigo) o Z0 é o do perfil e
/// o programa sai igual a antes.
/// </summary>
public static class LatheZero
{
    /// <summary>Nome gravado no arquivo.</summary>
    public static string FileName(LatheZeroFace f) => f == LatheZeroFace.Finished ? "face-pronta" : "face-oposta";

    /// <summary>Tipo a partir do nome do arquivo (null = desconhecido).</summary>
    public static LatheZeroFace? FromFileName(string name) => name switch
    {
        "face-pronta" => LatheZeroFace.Finished,
        "face-oposta" => LatheZeroFace.Opposite,
        _ => null,
    };

    /// <summary>Nomes da tela: "Z0 do desenho do perfil" (null), "Face pronta", "Face oposta".</summary>
    public static readonly IReadOnlyList<string> ChoiceNames = ["Z0 do desenho do perfil", "Face pronta", "Face oposta"];

    /// <summary>Problema que trava Gerar (null = pronto): face escolhida sem perfil para medir onde ela fica.</summary>
    public const string NeedsProfile = "Z0 na face escolhida precisa do perfil da peça";

    /// <summary>Z (no desenho do perfil) onde fica a face escolhida; null = sem escolha ou sem perfil.</summary>
    public static double? FaceZ(LatheJob job) => job.ZeroFace switch
    {
        null => null,
        _ when job.Profile is null => null,
        LatheZeroFace.Finished => job.Profile.Start.X,
        _ => job.Profile.End.X,
    };

    /// <summary>Quanto somar aos Z do programa (0 = nada muda).</summary>
    public static double Shift(LatheJob job) => FaceZ(job) is { } z ? -z : 0;

    /// <summary>Problemas que travam Gerar (vazio = pronto).</summary>
    public static IReadOnlyList<string> Problems(LatheJob job) => job.ZeroFace is not null && job.Profile is null ? [NeedsProfile] : [];

    /// <summary>Operação com todos os Z somados de <paramref name="dz"/> (movimentos, Z seguro, ciclo de perfil e rosca).</summary>
    public static LatheOperation Apply(LatheOperation op, double dz)
    {
        if (dz == 0)
        {
            return op;
        }

        var shift = new Point2(dz, 0);
        LatheCycle? cycle = op.Cycle is { } c
            ? c with
            {
                StartZ = c.StartZ + dz,
                Profile = new LatheProfile(c.Profile.Segments.Select(s => s with { Start = s.Start + shift, End = s.End + shift, Center = s.IsArc ? s.Center + shift : s.Center }).ToList()),
            }
            : null;
        return op with
        {
            SafeZ = op.SafeZ + dz,
            Moves = op.Moves.Select(m => m with { Z = m.Z + dz }).ToList(),
            Cycle = cycle,
            Thread = op.Thread is { } t ? t with { StartZ = t.StartZ + dz, EndZ = t.EndZ + dz } : null,
        };
    }

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Texto do Z0 para a árvore e o cartão Zero.</summary>
    public static string Title(LatheJob job) => job.ZeroFace switch
    {
        null => "Z0 do perfil",
        LatheZeroFace.Finished => "Z0 na face pronta" + At(job),
        _ => "Z0 na face oposta" + At(job),
    };

    private static string At(LatheJob job) => FaceZ(job) is { } z ? $" (Z {z.ToString("0.###", Br)} do desenho)" : " (falta o perfil)";

    /// <summary>Comentário do programa (sem acento); null quando o Z0 é o do perfil (programa igual ao antigo).</summary>
    public static string? ProgramNote(LatheJob job) => job.ZeroFace switch
    {
        null => null,
        LatheZeroFace.Finished => "Z0 NA FACE PRONTA",
        _ => "Z0 NA FACE OPOSTA",
    };
}
