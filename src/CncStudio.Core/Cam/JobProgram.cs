using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>
/// Programa nº e G5x do Trabalho de fresa (UX-04g PR A). Trabalho novo nasce com os dois vazios (null): nada suposto. Sem valor, o
/// trabalho fica Incompleto e Gerar e Enviar travam; Salvar nunca trava. Arquivo antigo traz os dois e gera igual.
/// O Siemens não escreve o número no programa (sai só o nome do trabalho), então nele o número não é exigido.
/// </summary>
public static class CamJobProgram
{
    /// <summary>Nome do campo do número do programa (como na tela).</summary>
    public const string ProgramNumberField = "Programa nº";

    /// <summary>Nome do campo do zero de peça (como na tela).</summary>
    public const string WorkOffsetField = "G5x";

    /// <summary>Menor e maior número de programa aceitos no Fanuc (O0001 a O9999).</summary>
    public const int MinProgramNumber = 1, MaxProgramNumber = 9999;

    /// <summary>Frase do campo do número quando o controle é Siemens.</summary>
    public const string SiemensUsesName = "Siemens usa o nome do trabalho";

    /// <summary>Pendência de número vazio (Fanuc).</summary>
    public const string ProgramNumberMissing = "Falta o Programa nº";

    /// <summary>Pendência de G5x vazio.</summary>
    public const string WorkOffsetMissing = "Falta o G5x";

    /// <summary>
    /// G5x que a tela oferece por controle: Fanuc G54 a G59; Siemens G54 a G57 (no Siemens, G58 e G59 são deslocamentos
    /// programáveis, não zeros ajustáveis). G54.1 P não é oferecido.
    /// </summary>
    public static IReadOnlyList<int> WorkOffsets(CamDialect dialect) =>
        dialect == CamDialect.Siemens ? [54, 55, 56, 57] : [54, 55, 56, 57, 58, 59];

    /// <summary>O controle exige o número do programa? Só o Fanuc (o Siemens não escreve o número).</summary>
    public static bool NeedsProgramNumber(CamDialect dialect) => dialect == CamDialect.Fanuc;

    /// <summary>Motivo do número ser inválido (fora de 1 a 9999); null se vale.</summary>
    public static string? ProgramNumberError(int number) =>
        number is >= MinProgramNumber and <= MaxProgramNumber ? null : $"Programa nº {number} inválido: use um inteiro de {MinProgramNumber} a {MaxProgramNumber}.";

    /// <summary>Motivo do G5x não valer no controle; null se vale.</summary>
    public static string? WorkOffsetError(int code, CamDialect dialect)
    {
        IReadOnlyList<int> list = WorkOffsets(dialect);
        return list.Contains(code) ? null
            : $"G{code} não serve no {(dialect == CamDialect.Siemens ? "Siemens" : "Fanuc")}: escolha de G{list[0]} a G{list[^1]}.";
    }

    /// <summary>
    /// Texto digitado no campo do número: vazio = null sem erro; inteiro de 1 a 9999 = o número; o resto (texto, decimal, fora da faixa)
    /// = null com o motivo (o número antigo não vale).
    /// </summary>
    public static (int? Number, string? Error) ParseProgramNumber(string? text)
    {
        string t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            return (null, null);
        }

        if (!int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n))
        {
            return (null, $"\"{t}\" não é um número inteiro. {ProgramNumberMissing}: use de {MinProgramNumber} a {MaxProgramNumber}.");
        }

        return ProgramNumberError(n) is { } bad ? (null, bad) : (n, null);
    }

    /// <summary>
    /// O que falta ou não vale no número e no G5x, na ordem da tela (vazio = pronto). Cada linha trava Gerar e Enviar; nenhuma trava
    /// Salvar. Siemens: o número não conta.
    /// </summary>
    public static IReadOnlyList<string> Problems(CamJob job)
    {
        var list = new List<string>();
        if (NeedsProgramNumber(job.Dialect))
        {
            if (job.ProgramNumber is not { } n)
            {
                list.Add(ProgramNumberMissing);
            }
            else if (ProgramNumberError(n) is { } bad)
            {
                list.Add(bad);
            }
        }

        if (job.WorkOffset is not { } g)
        {
            list.Add(WorkOffsetMissing);
        }
        else if (WorkOffsetError(g, job.Dialect) is { } bad)
        {
            list.Add(bad);
        }

        return list;
    }

    /// <summary>A pendência é do número ou do G5x (a barra leva ao cartão da Fixação)?</summary>
    public static bool IsProgramProblem(string pending) =>
        pending == ProgramNumberMissing || pending == WorkOffsetMissing
        || pending.StartsWith("Programa nº ", StringComparison.Ordinal) || (pending.StartsWith('G') && pending.Contains(" não serve no ", StringComparison.Ordinal));

    /// <summary>Precisa da versão 3 do arquivo: o número ou o G5x está vazio (o campo não vai para o arquivo).</summary>
    public static bool NeedsVersion3(CamJob job) => job.ProgramNumber is null || job.WorkOffset is null;

    /// <summary>Texto do rodapé: "Programa nº 1001 · G54", com "falta" no que estiver vazio; Siemens sem o número.</summary>
    public static string StatusText(CamJob job)
    {
        string g = job.WorkOffset is { } w ? "G" + w.ToString(CultureInfo.InvariantCulture) : "G5x: falta";
        if (!NeedsProgramNumber(job.Dialect))
        {
            return g;
        }

        string p = job.ProgramNumber is { } n ? "Programa nº " + n.ToString(CultureInfo.InvariantCulture) : "Programa nº: falta";
        return p + " · " + g;
    }
}
