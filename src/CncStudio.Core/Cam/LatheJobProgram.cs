using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>
/// Programa nº, G5x e refrigeração do Trabalho de torno (TORNO-T1), com a mesma regra da fresa (<see cref="CamJobProgram"/>): trabalho
/// novo nasce com os três vazios (nada suposto); vazio deixa o trabalho Incompleto e trava Gerar e Enviar; Salvar nunca trava.
/// Arquivo antigo traz os três e gera igual. O Siemens não escreve o número (sai só o nome), então nele o número não é exigido.
/// </summary>
public static class LatheJobProgram
{
    /// <summary>Nome do campo da refrigeração (como na tela).</summary>
    public const string CoolantField = "Refrigeração";

    /// <summary>Pendência de refrigeração vazia.</summary>
    public const string CoolantMissing = "Falta a refrigeração";

    /// <summary>
    /// O que falta no número (Fanuc), no G5x e na refrigeração, na ordem da tela (vazio = pronto). Cada linha trava Gerar e Enviar;
    /// nenhuma trava Salvar.
    /// </summary>
    public static IReadOnlyList<string> Problems(LatheJob job) => Problems(job.Dialect, job.ProgramNumber, job.WorkOffset, job.Coolant);

    /// <summary>A mesma conferência para valores soltos (uma fixação do trabalho).</summary>
    public static IReadOnlyList<string> Problems(CamDialect dialect, int? programNumber, int? workOffset, bool? coolant)
    {
        var list = new List<string>();
        if (CamJobProgram.NeedsProgramNumber(dialect) && programNumber is null)
        {
            list.Add(CamJobProgram.ProgramNumberMissing);
        }

        if (workOffset is null)
        {
            list.Add(CamJobProgram.WorkOffsetMissing);
        }

        if (coolant is null)
        {
            list.Add(CoolantMissing);
        }

        return list;
    }

    /// <summary>
    /// Avisos (não travam) de valores do arquivo fora do que a tela oferece: número fora de 1 a 9999 (Fanuc) ou G5x fora da lista do
    /// controle. O programa sai como antes; o usuário confere no controle.
    /// </summary>
    public static IReadOnlyList<string> Warnings(CamDialect dialect, int? programNumber, int? workOffset)
    {
        var list = new List<string>();
        if (CamJobProgram.NeedsProgramNumber(dialect) && programNumber is { } n && CamJobProgram.ProgramNumberError(n) is not null)
        {
            list.Add($"Programa nº {n} veio do arquivo e fica fora de {CamJobProgram.MinProgramNumber} a {CamJobProgram.MaxProgramNumber}: o programa sai como antes; confira no controle.");
        }

        if (workOffset is { } g && CamJobProgram.WorkOffsetError(g, dialect) is not null)
        {
            IReadOnlyList<int> offsets = CamJobProgram.WorkOffsets(dialect);
            list.Add($"G{g} veio do arquivo e não está na lista do {(dialect == CamDialect.Siemens ? "Siemens" : "Fanuc")} (G{offsets[0]} a G{offsets[^1]}): o programa sai como antes; confira no controle.");
        }

        return list;
    }

    /// <summary>A pendência é do número, do G5x ou da refrigeração (a barra leva ao cartão da Fixação)?</summary>
    public static bool IsProgramProblem(string pending) => CamJobProgram.IsProgramProblem(pending) || pending == CoolantMissing;

    /// <summary>Texto do rodapé: "Programa nº 1002 · G54", com "falta" no que estiver vazio; Siemens sem o número.</summary>
    public static string StatusText(CamDialect dialect, int? programNumber, int? workOffset)
    {
        string g = workOffset is { } w ? "G" + w.ToString(CultureInfo.InvariantCulture) : "G5x: falta";
        if (!CamJobProgram.NeedsProgramNumber(dialect))
        {
            return g;
        }

        string p = programNumber is { } n ? "Programa nº " + n.ToString(CultureInfo.InvariantCulture) : "Programa nº: falta";
        return p + " · " + g;
    }
}
