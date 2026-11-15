using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Preparação do Trabalho torno (TORNO-T1): cartão Máquina (o mesmo editor de perfil da fresa: escolher, criar e editar), Programa nº,
/// G5x e refrigeração digitados (vazio trava Gerar e Enviar, nunca Salvar) e o bruto do trabalho (Ø, comprimento e Z da face).
/// </summary>
public partial class LatheJobView
{
    private static readonly IBrush Muted = Brush.Parse("#9aa7b5");
    private static readonly IBrush Accent = Brush.Parse("#ff8a1f");

    private static TextBlock CardTitle(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeight.Bold };

    private static TextBlock Caps(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    /// <summary>Botão de escolha da casca (laranja quando escolhido). Sem <paramref name="click"/>, só mostra.</summary>
    private static Button Choice(string text, bool selected, Action? click, string? tip = null)
    {
        var b = new Button
        {
            Content = text, Padding = new Thickness(10, 4), FontSize = 12, MinHeight = 0,
            Background = selected ? Brush.Parse("#2b1a0a") : Brush.Parse("#0d1117"),
            BorderBrush = selected ? Accent : Brush.Parse("#2a3442"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Foreground = selected ? Brush.Parse("#ffb066") : Brushes.White,
            IsEnabled = click is not null || selected,
        };
        if (click is not null)
        {
            b.Click += (_, _) => click();
        }

        if (tip is not null)
        {
            ToolTip.SetTip(b, tip);
            ToolTip.SetShowOnDisabled(b, true);
        }

        return b;
    }

    private static WrapPanel Choices(params Control[] items)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Control c in items)
        {
            c.Margin = new Thickness(0, 0, 6, 4);
            p.Children.Add(c);
        }

        return p;
    }

    // ---------------------------------------------------------------- Máquina

    /// <summary>Perfis salvos (config.json), os mesmos da fresa.</summary>
    public Func<List<MachineProfile>>? ProfilesSource { get; set; }

    /// <summary>Nome do perfil ativo.</summary>
    public Func<string>? ActiveMachineName { get; set; }

    /// <summary>Troca o perfil ativo (o app grava e refaz o que depende dele).</summary>
    public Action<string>? SetActiveMachine { get; set; }

    /// <summary>Grava os perfis no config.json.</summary>
    public Action? SaveMachines { get; set; }

    /// <summary>Abre a tela completa (Perfil 4/5 eixos).</summary>
    public Action? OpenMachineProfiles { get; set; }

    /// <summary>Formulário aberto: null = só mostra; -1 = perfil novo; ≥ 0 = editando aquele perfil da lista.</summary>
    private int? _machineEdit;

    private string _mName = "";
    private CamDialect? _mController;
    private string? _mAxes;
    private string _mRpm = "";
    private string? _mErrors;

    /// <summary>Cartão Máquina: perfil ativo (escolher entre os salvos), criar e editar. O torno usa dele o controle e a rotação máxima.</summary>
    private void MachineCard(StackPanel card)
    {
        card.Children.Clear();
        if (_machineEdit is not null)
        {
            MachineForm(card);
            return;
        }

        List<MachineProfile> all = ProfilesSource?.Invoke() ?? [];
        MachineProfile? m = MachineSource?.Invoke();
        var head = new DockPanel();
        if (all.Count > 0)
        {
            var pick = new ComboBox { MinWidth = 150, FontSize = 12, PlaceholderText = "Escolher perfil" };
            foreach (MachineProfile p in all)
            {
                pick.Items.Add(new ComboBoxItem { Content = p.Name });
            }

            pick.SelectedIndex = m is null ? -1 : all.FindIndex(p => p.Name == m.Name);
            pick.SelectionChanged += (_, _) =>
            {
                if (pick.SelectedIndex >= 0 && pick.SelectedIndex < all.Count && all[pick.SelectedIndex].Name != m?.Name)
                {
                    SetActiveMachine?.Invoke(all[pick.SelectedIndex].Name);
                    SaveMachines?.Invoke();
                    Refresh(keep: true);
                }
            };
            ToolTip.SetTip(pick, "Escolher o perfil ativo entre os salvos.");
            DockPanel.SetDock(pick, Dock.Right);
            head.Children.Add(pick);
        }

        head.Children.Add(CardTitle("Máquina"));
        card.Children.Add(head);
        if (m is null)
        {
            card.Children.Add(UiKit.StatusNote("Nenhum perfil ativo", "pending"));
            card.Children.Add(Note(all.Count > 0
                ? "O perfil define o controle e a rotação máxima do fuso. Escolha um já cadastrado ou crie um novo."
                : "O perfil define o controle e a rotação máxima do fuso. Crie o primeiro."));
            card.Children.Add(Choices(Choice("+ Criar perfil", false, () => OpenMachineForm(null))));
            card.Children.Add(UiKit.StatusNote("Sem perfil, a rotação das operações não é conferida com o limite do fuso.", "pending"));
            return;
        }

        card.Children.Add(Caps("PERFIL"));
        card.Children.Add(new TextBlock { Text = m.Name, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        card.Children.Add(Caps("CONTROLE"));
        card.Children.Add(Choices(Choice("Fanuc", m.Controller == CamDialect.Fanuc, null), Choice("Siemens", m.Controller == CamDialect.Siemens, null)));
        if (CamJobInfo.MachineControlWarning(m, _job.Dialect) is { } warning)
        {
            card.Children.Add(UiKit.StatusNote(warning, "pending"));
        }

        card.Children.Add(Caps("EIXOS"));
        card.Children.Add(new TextBlock { Text = CamJobInfo.MachineAxesLabel(m) + " (o torno não usa os eixos do perfil)", FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        card.Children.Add(Caps("ROTAÇÃO MÁX."));
        card.Children.Add(new TextBlock
        {
            Text = m.MaxSpindleRpm is { } rpm ? rpm.ToString("0", Br) + " rpm" : "vazia (limite do fuso não verificado)",
            FontSize = 13, Foreground = m.MaxSpindleRpm is null ? Muted : Brushes.White,
        });
        int index = all.FindIndex(p => p.Name == m.Name);
        card.Children.Add(Choices(
            Choice("Editar", false, index >= 0 ? () => OpenMachineForm(index) : null),
            Choice("+ Criar perfil", false, () => OpenMachineForm(null)),
            Choice("Tela completa…", false, OpenMachineProfiles, "Pivôs, limites, modo de saída e excluir perfil: Mais → Perfil 4/5 eixos.")));
    }

    /// <summary>Abre o formulário: perfil novo (tudo vazio, nada suposto) ou cópia do perfil para editar.</summary>
    private void OpenMachineForm(int? index)
    {
        List<MachineProfile> all = ProfilesSource?.Invoke() ?? [];
        MachineProfile? p = index is { } i && i < all.Count ? all[i] : null;
        _machineEdit = p is null ? -1 : index;
        _mName = p?.Name ?? "";
        _mController = p?.Controller;
        _mAxes = p is null ? null : CamJobInfo.MachineAxesLabel(p);
        _mRpm = p?.MaxSpindleRpm is { } rpm ? rpm.ToString("0.##", Br) : "";
        _mErrors = null;
        MachineCard(PrepMachine);
    }

    /// <summary>Formulário curto do perfil (o da fresa): nome, controle, eixos e rotação máxima, com a validação do Core.</summary>
    private void MachineForm(StackPanel card)
    {
        bool creating = _machineEdit == -1;
        card.Children.Add(CardTitle(creating ? "Criar perfil" : "Editar perfil"));
        void Field(string label, string value, string watermark, Action<string> set, string? emptyMark)
        {
            var box = new TextBox { Text = value, MinHeight = 26, Padding = new Thickness(6, 3), Watermark = watermark, FontSize = 12 };
            (Grid field, Action<string?> mark) = CamShell.DashedField(box);
            void Remark() => mark(emptyMark is not null && string.IsNullOrWhiteSpace(box.Text) ? emptyMark : null);
            box.TextChanged += (_, _) =>
            {
                set(box.Text ?? "");
                Remark();
            };
            Remark();
            card.Children.Add(Caps(label));
            card.Children.Add(field);
        }

        Field("NOME", _mName, "digite o nome", v => _mName = v, "Falta o nome do perfil.");
        card.Children.Add(Caps("CONTROLE"));
        card.Children.Add(Choices(
            Choice("Fanuc", _mController == CamDialect.Fanuc, () => PickForm(() => _mController = CamDialect.Fanuc)),
            Choice("Siemens", _mController == CamDialect.Siemens, () => PickForm(() => _mController = CamDialect.Siemens))));
        if (_mController is null)
        {
            card.Children.Add(Note("Escolha o controle da máquina."));
        }

        card.Children.Add(Caps("EIXOS"));
        var axes = CamJobInfo.MachineAxesOptions.Select(o => (Control)Choice(o, _mAxes == o, () => PickForm(() => _mAxes = o))).ToList();
        if (_mAxes == CamJobInfo.MachineAxesCustom)
        {
            axes.Add(Choice("Personalizado", true, () => { }, "Os eixos deste perfil não cabem nos botões: ficam como estão."));
        }

        card.Children.Add(Choices([.. axes]));
        card.Children.Add(Note("O perfil é o mesmo da fresa: os eixos valem para ela. O torno usa o controle e a rotação máxima."));
        Field("ROTAÇÃO MÁX.", _mRpm, "digite (rpm)", v => _mRpm = v, null);
        card.Children.Add(Note("Vazia não bloqueia: o limite do fuso fica \"não verificado\"."));
        if (_mErrors is { } errors)
        {
            card.Children.Add(UiKit.StatusNote(errors, "bad"));
        }

        card.Children.Add(Choices(
            Choice("Salvar perfil", true, SaveMachineForm),
            Choice("Cancelar", false, () =>
            {
                _machineEdit = null;
                MachineCard(PrepMachine);
            })));
    }

    private void PickForm(Action change)
    {
        change();
        MachineCard(PrepMachine);
    }

    /// <summary>Salva o perfil com a mesma validação da tela completa; o perfil criado ou editado fica ativo.</summary>
    private void SaveMachineForm()
    {
        List<MachineProfile>? all = ProfilesSource?.Invoke();
        if (all is null || _machineEdit is not { } edit)
        {
            return;
        }

        int? index = edit >= 0 ? edit : null;
        MachineProfile? editing = index is { } i && i < all.Count ? all[i] : null;
        string? axes = _mAxes == CamJobInfo.MachineAxesCustom ? null : _mAxes;
        (MachineProfile? profile, IReadOnlyList<string> errors) = MachineProfileForm.Build(editing, _mName, _mController, axes, _mRpm);
        string? error = errors.Count > 0 ? string.Join("\n", errors.Select(e => "• " + e)) : null;
        string active = ActiveMachineName?.Invoke() ?? "";
        if (profile is not null)
        {
            (active, string? saveError) = MachineProfileForm.Save(all, index, profile, active);
            error = saveError is null ? null : "• " + saveError;
        }

        if (error is not null)
        {
            _mErrors = error;
            MachineCard(PrepMachine);
            return;
        }

        _machineEdit = null;
        SetActiveMachine?.Invoke(active);
        SaveMachines?.Invoke();
        Refresh(keep: true);
    }

    // ---------------------------------------------------------------- Programa nº, G5x e refrigeração

    /// <summary>Motivo do último número digitado que não valeu (texto, decimal, fora de 1 a 9999); some ao digitar um que vale.</summary>
    private string? _programTypedError;

    private const string MissingLock = "Falta este valor. Sem ele, Gerar e Enviar ficam travados.";

    /// <summary>
    /// Programa nº, G5x e refrigeração (TORNO-T1): digitados, sem padrão. Vazio: tracejado laranja com o motivo; o trabalho fica
    /// Incompleto e Gerar e Enviar travam (Salvar não). Siemens: o número não é usado (sai o nome do trabalho).
    /// </summary>
    private void ProgramFields()
    {
        IReadOnlyList<string> problems = LatheJobProgram.Problems(_job);
        if (problems.Count > 0)
        {
            var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            note.Children.Add(CamShell.Badge("!", "pending"));
            note.Children.Add(new TextBlock
            {
                Text = "Incompleto: " + string.Join(" · ", problems) + ".", FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = UiKit.Status("pending").Fg, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center,
            });
            PropsPanel.Children.Add(note);
        }

        foreach (string warning in LatheJobProgram.Warnings(_job.Dialect, _job.ProgramNumber, _job.WorkOffset))
        {
            PropsPanel.Children.Add(UiKit.StatusNote("Aviso: " + warning, "pending"));
        }

        PropsPanel.Children.Add(new TextBlock { Text = CamJobProgram.ProgramNumberField, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        if (CamJobProgram.NeedsProgramNumber(_job.Dialect))
        {
            var number = new TextBox
            {
                Text = _job.ProgramNumber?.ToString(CultureInfo.InvariantCulture) ?? "", Watermark = "digite (1 a 9999)", MinHeight = 26, MaxLength = 8,
            };
            (Grid field, Action<string?> mark) = CamShell.DashedField(number);
            mark(_programTypedError ?? (_job.ProgramNumber is null ? MissingLock : null));
            string shown = number.Text;
            void Commit()
            {
                // sair do campo sem mexer não muda nada (o número de arquivo antigo fora da faixa continua valendo)
                if ((number.Text ?? "") == shown)
                {
                    return;
                }

                (int? value, string? why) = CamJobProgram.ParseProgramNumber(number.Text);
                if (value == _job.ProgramNumber && why == _programTypedError)
                {
                    return;
                }

                _programTypedError = why;
                _job.ProgramNumber = value;
                Refresh(keep: true);
            }

            number.LostFocus += (_, _) => Commit();
            number.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                }
            };
            PropsPanel.Children.Add(field);
        }
        else
        {
            PropsPanel.Children.Add(new TextBlock { Text = CamJobProgram.SiemensUsesName + ".", Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        }

        PropsPanel.Children.Add(new TextBlock { Text = "Zero de peça (" + CamJobProgram.WorkOffsetField + ")", FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
        IReadOnlyList<int> offsets = CamJobProgram.WorkOffsets(_job.Dialect);
        var offset = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Escolha o G5x…" };
        foreach (int code in offsets)
        {
            offset.Items.Add(new ComboBoxItem { Content = "G" + code.ToString(CultureInfo.InvariantCulture) });
        }

        // valor do arquivo fora da lista (ex.: G58 com Siemens): aparece como item "do arquivo", escolhido e sem tracejado (só o aviso)
        int index = _job.WorkOffset is { } g ? offsets.ToList().IndexOf(g) : -1;
        if (_job.WorkOffset is { } fileOffset && index < 0)
        {
            offset.Items.Add(new ComboBoxItem { Content = "G" + fileOffset.ToString(CultureInfo.InvariantCulture) + " · do arquivo" });
            index = offsets.Count;
        }

        offset.SelectedIndex = index;
        (Grid offsetField, Action<string?> offsetMark) = CamShell.DashedField(offset);
        offsetMark(_job.WorkOffset is null ? MissingLock : null);
        offset.SelectionChanged += (_, _) =>
        {
            if (offset.SelectedIndex >= 0 && offset.SelectedIndex < offsets.Count && offsets[offset.SelectedIndex] != _job.WorkOffset)
            {
                _job.WorkOffset = offsets[offset.SelectedIndex];
                Refresh(keep: true);
            }
        };
        PropsPanel.Children.Add(offsetField);

        PropsPanel.Children.Add(new TextBlock { Text = LatheJobProgram.CoolantField + " (M8/M9)", FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
        var coolant = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Escolha: ligada ou desligada…" };
        coolant.Items.Add(new ComboBoxItem { Content = "Ligada (M8 no início, M9 no fim)" });
        coolant.Items.Add(new ComboBoxItem { Content = "Desligada (sem M8/M9)" });
        coolant.SelectedIndex = _job.Coolant switch { true => 0, false => 1, _ => -1 };
        (Grid coolantField, Action<string?> coolantMark) = CamShell.DashedField(coolant);
        coolantMark(_job.Coolant is null ? MissingLock : null);
        coolant.SelectionChanged += (_, _) =>
        {
            bool? now = coolant.SelectedIndex switch { 0 => true, 1 => false, _ => null };
            if (now is not null && now != _job.Coolant)
            {
                _job.Coolant = now;
                Refresh(keep: true);
            }
        };
        PropsPanel.Children.Add(coolantField);
    }

    // ---------------------------------------------------------------- bruto do trabalho

    /// <summary>
    /// Campo numérico opcional da Preparação (vazio = não informado, nada suposto). Valor inválido fica em vermelho com o motivo e não
    /// é gravado. <paramref name="set"/> recebe o valor novo; a tela é refeita sem refazer os cartões (o foco segue).
    /// </summary>
    private void OptNum(string label, double? value, Action<double?> set, bool positive = true, string unit = "mm")
    {
        var box = new TextBox { Text = value is { } v0 ? Fmt(v0) : "", Watermark = "não informado", MinHeight = 26 };
        void Commit()
        {
            double? v = null;
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                double parsed = ParseNum(box.Text);
                if (double.IsNaN(parsed) || (positive && !(parsed > 0)))
                {
                    box.Foreground = Brushes.IndianRed;
                    Errors.Text = double.IsNaN(parsed) ? $"• Valor inválido em \"{label}\"." : $"• \"{label}\" precisa ser maior que zero (ou deixe em branco).";
                    return;
                }

                v = parsed;
            }

            box.ClearValue(TemplatedControl.ForegroundProperty);
            if (v != value)
            {
                value = v;
                set(v);
                Refresh(keep: true, props: false);
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        PropsPanel.Children.Add(Row(label, box, unit));
    }

    /// <summary>Grava um campo do bruto do trabalho; tudo vazio = sem bruto (nada vai para o arquivo).</summary>
    private void SetStock(Func<LatheJobStock, LatheJobStock> change)
    {
        LatheJobStock next = change(_job.Stock ?? new LatheJobStock());
        _job.Stock = next.IsEmpty ? null : next;
    }

    /// <summary>
    /// Bruto do trabalho (TORNO-T1): Ø, comprimento e Z da face do bruto, digitados. As operações novas nascem com esse Ø; as que já
    /// existem mantêm o seu (lista abaixo, só leitura, com atalho para a operação).
    /// </summary>
    private void StockProps()
    {
        PropsPanel.Children.Add(CardTitle("Bruto (barra)"));
        PropsPanel.Children.Add(Note("Digite a barra do trabalho. As operações novas de torneamento externo e faceamento nascem com este Ø; as que já existem mantêm o delas. Nada disto entra no G-code."));
        OptNum("Ø do bruto", _job.Stock?.Diameter, v => SetStock(s => s with { Diameter = v }));
        OptNum("Comprimento", _job.Stock?.Length, v => SetStock(s => s with { Length = v }));
        OptNum(LatheJobInfo.StockFaceZField, _job.Stock?.FaceZ, v => SetStock(s => s with { FaceZ = v }), positive: false);
        PropsPanel.Children.Add(Note("Z da face do bruto: onde a ponta da barra fica em relação ao Z0 do perfil (ex.: 2 = 2 mm de sobra para facear)."));
        var groups = LatheJobInfo.StockDiameters(_job);
        if (groups.Count == 0)
        {
            return;
        }

        PropsPanel.Children.Add(Caps("Ø DO BRUTO EM CADA OPERAÇÃO"));
        foreach ((string text, IReadOnlyList<int> ops) in groups)
        {
            foreach (int op in ops)
            {
                int index = op;
                var go = new Button
                {
                    Content = $"Ø {text} mm · {LatheJobInfo.OpTitle(op, _job.Operations[op])}", FontSize = 12, Padding = new Thickness(8, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                ToolTip.SetTip(go, "Abrir a operação na aba Operações");
                go.Click += (_, _) => GoToOp(index);
                PropsPanel.Children.Add(go);
            }
        }
    }
}
