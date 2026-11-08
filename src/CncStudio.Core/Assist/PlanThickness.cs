using System.Globalization;

namespace CncStudio.Core.Assist;

/// <summary>
/// Campo "espessura do bruto" do plano do Assist (adendo 3 do UX-04f): o texto acompanha a espessura que o Trabalho fresa informa,
/// sem supor nada. O valor que veio do trabalho some quando o trabalho deixa de informar (cilindro, tubo, incompleto, sem bruto ou
/// bloco todo acima de Z0); o valor digitado à mão nunca é apagado.
/// </summary>
public static class PlanThickness
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Novo texto do campo e se ele veio do trabalho. <paramref name="text"/> e <paramref name="fromJob"/>: o estado atual.
    /// <paramref name="jobThickness"/>: a espessura que o trabalho informa agora (null = não informa).
    /// <paramref name="fill"/>: false só atualiza ou esvazia um valor que já veio do trabalho (não troca o digitado nem o inicial).
    /// </summary>
    public static (string Text, bool FromJob) Sync(string text, bool fromJob, double? jobThickness, bool fill = true)
    {
        if (jobThickness is { } t && double.IsFinite(t) && t > 0)
        {
            return fill || fromJob ? (t.ToString("0.##", Br), true) : (text, false);
        }

        // o trabalho não informa: o que veio dele não pode sobrar; o digitado fica
        return fromJob ? ("", false) : (text, false);
    }
}
