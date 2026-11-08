using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>
/// Formulário curto do cartão Máquina do Trabalho fresa (UX-04f PR 2): nome, controle, eixos (os 4 botões) e rotação máxima.
/// Fica no Core para ter teste. A validação é a mesma do perfil completo (<see cref="MachineProfile.Validate"/>, usada pela tela
/// Perfil 4/5 eixos); o formulário só acrescenta o que ele não sabe: controle escolhido e nome único.
/// </summary>
public static class MachineProfileForm
{
    /// <summary>
    /// Monta o perfil a partir do formulário. Editar parte de uma cópia do perfil salvo e só troca o que o formulário mostra: o que
    /// ele não mostra (pivôs, limites, sentido, modo de saída) fica como estava. <paramref name="axes"/> null ou igual ao botão atual =
    /// mantém os eixos do perfil (inclusive o "personalizado"); outro botão = eixos do modelo pronto daquele botão.
    /// Nada suposto: nome e controle precisam ser preenchidos; a rotação pode ficar vazia (texto vazio), mas texto que não é
    /// número é erro.
    /// </summary>
    public static (MachineProfile? Profile, IReadOnlyList<string> Errors) Build(MachineProfile? editing, string name, CamDialect? controller, string? axes, string rpmText)
    {
        var errors = new List<string>();
        MachineProfile p = editing?.Clone() ?? new MachineProfile();
        p.Name = (name ?? "").Trim();
        if (controller is { } c)
        {
            p.Controller = c;
        }
        else
        {
            errors.Add("Escolha o controle (Fanuc ou Siemens).");
        }

        if (axes is not null && (editing is null || axes != CamJobInfo.MachineAxesLabel(editing)))
        {
            if (Template(axes) is not { } t)
            {
                errors.Add("Escolha os eixos.");
            }
            else
            {
                p.Axes = t.Axes;
            }
        }
        else if (axes is null && editing is null)
        {
            errors.Add("Escolha os eixos.");
        }

        string typed = (rpmText ?? "").Trim();
        if (typed.Length == 0)
        {
            p.MaxSpindleRpm = null;
        }
        else if (double.TryParse(typed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double rpm))
        {
            p.MaxSpindleRpm = rpm;
        }
        else
        {
            errors.Add("Rotação máxima do fuso: número inválido (ou deixe em branco).");
        }

        errors.AddRange(p.Validate());
        return errors.Count > 0 ? (null, errors) : (p, []);
    }

    /// <summary>Modelo pronto de cada botão de eixos (os de <see cref="MachineProfile"/>); null para texto que não é botão.</summary>
    public static MachineProfile? Template(string axes) => axes switch
    {
        _ when axes == CamJobInfo.MachineAxesOptions[0] => MachineProfile.ThreeAxis(),
        _ when axes == CamJobInfo.MachineAxesOptions[1] => MachineProfile.FourAxisTableA(),
        _ when axes == CamJobInfo.MachineAxesOptions[2] => MachineProfile.FiveAxisTableTable(),
        _ when axes == CamJobInfo.MachineAxesOptions[3] => MachineProfile.FiveAxisMixed(),
        _ => null,
    };

    /// <summary>
    /// Grava o perfil na lista (o app salva a lista no config.json): novo no fim ou no lugar do que foi editado. Nome repetido em
    /// outro perfil é recusado (mesma regra da tela Perfil 4/5 eixos). Devolve o nome do perfil ativo depois de salvar: o novo
    /// perfil passa a ser o ativo; renomear o ativo leva o nome junto. Erro = nada muda.
    /// </summary>
    public static (string Active, string? Error) Save(List<MachineProfile> all, int? editIndex, MachineProfile profile, string active)
    {
        if (editIndex is { } i && (i < 0 || i >= all.Count))
        {
            return (active, "Perfil a editar não existe mais.");
        }

        int same = all.FindIndex(x => x.Name == profile.Name);
        if (same >= 0 && same != editIndex)
        {
            return (active, "Já existe um perfil com esse nome. Escolha outro nome.");
        }

        if (editIndex is { } at)
        {
            string old = all[at].Name;
            all[at] = profile;
            return (old == active ? profile.Name : active, null);
        }

        all.Add(profile);
        return (profile.Name, null);
    }
}
