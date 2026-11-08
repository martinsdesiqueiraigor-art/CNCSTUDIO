using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Step;

namespace CncStudio.App.Modules;

/// <summary>
/// Cartões Máquina (escolher, criar e editar perfil), Peça pronta e Bruto da aba Preparação (UX-04f). O bruto e a altura da peça nunca entram no G-code;
/// campo vazio ou inválido fica com o tracejado laranja e deixa o bruto incompleto: trava Gerar e Enviar, como o "Bruto não definido",
/// mas nunca trava Salvar (adendo 2 de 08/11).
/// </summary>
public partial class JobView
{
    private static readonly IBrush Muted = Brush.Parse("#9aa7b5");
    private static readonly IBrush Accent = Brush.Parse("#ff8a1f");

    /// <summary>Remarcações dos campos do bruto e da altura (rodam depois de cada mudança, sem refazer os cartões).</summary>
    private readonly List<Action> _prepMarks = [];

    /// <summary>Bloco absoluto escolhido e ainda vazio (só tela: nada vai para o trabalho até "Definir bruto").</summary>
    private bool _absoluteMode;

    /// <summary>
    /// Memória da tela (achado B do adendo 2): o último bloco absoluto e os últimos valores de cada formato, para que voltar ao
    /// formato anterior os restaure. Só nesta sessão, nunca no arquivo; zera ao abrir ou criar outro trabalho.
    /// </summary>
    private JobStock? _lastAbsolute;

    private readonly Dictionary<StockShapeKind, JobStockShape> _lastShapes = [];

    /// <summary>Último modo do bloco usado (absoluto ou sobra), para o botão "Bloco" voltar a ele.</summary>
    private bool _lastBlockAbsolute;

    /// <summary>Esquece a memória dos formatos (outro trabalho aberto ou criado).</summary>
    private void ForgetStockMemory()
    {
        _lastAbsolute = null;
        _lastShapes.Clear();
        _lastBlockAbsolute = false;
    }

    /// <summary>Resumo do bruto no cartão (refeito a cada mudança).</summary>
    private ContentControl? _stockSummary;

    private static TextBlock CardTitle(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeight.Bold };

    private static TextBlock Caps(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    /// <summary>Botão de escolha da casca (laranja quando escolhido). Sem <paramref name="click"/>, só mostra (leitura ou "em breve").</summary>
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

    // ---------------------------------------------------------------- Máquina (PR 2: escolher, criar e editar)

    /// <summary>Perfis salvos (config.json). O cartão escolhe, cria e edita; excluir fica na tela completa.</summary>
    public Func<List<MachineProfile>>? ProfilesSource { get; set; }

    /// <summary>Nome do perfil ativo.</summary>
    public Func<string>? ActiveMachineName { get; set; }

    /// <summary>Troca o perfil ativo (o app grava e refaz o que depende dele).</summary>
    public Action<string>? SetActiveMachine { get; set; }

    /// <summary>Grava os perfis no config.json.</summary>
    public Action? SaveMachines { get; set; }

    /// <summary>Abre a tela completa (Perfil 4/5 eixos), para o que o cartão não mostra.</summary>
    public Action? OpenMachineProfiles { get; set; }

    /// <summary>Formulário aberto: null = só mostra; -1 = perfil novo; ≥ 0 = editando aquele perfil da lista.</summary>
    private int? _machineEdit;

    // rascunho do formulário (só tela até "Salvar perfil")
    private string _mName = "";
    private CamDialect? _mController;
    private string? _mAxes;
    private string _mRpm = "";
    private string? _mErrors;

    /// <summary>Cartão Máquina: perfil ativo (escolher entre os salvos), criar e editar (G2b, estados 4 e 5).</summary>
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
                ? "O perfil define eixos e rotação máxima. Escolha um já cadastrado ou crie um novo."
                : "O perfil define eixos e rotação máxima. Crie o primeiro."));
            card.Children.Add(Choices(Choice("+ Criar perfil", false, () => OpenMachineForm(null))));
            card.Children.Add(UiKit.StatusNote("Sem perfil, o CAM gera em 3 eixos. 4º eixo e texto no cilindro pedem perfil.", "pending"));
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
        string axes = CamJobInfo.MachineAxesLabel(m);
        var buttons = CamJobInfo.MachineAxesOptions.Select(o => (Control)Choice(o, o == axes, null)).ToList();
        if (axes == CamJobInfo.MachineAxesCustom)
        {
            buttons.Add(Choice("Personalizado", true, null, m.Describe()));
        }

        card.Children.Add(Choices([.. buttons]));
        if (axes == CamJobInfo.MachineAxesCustom)
        {
            card.Children.Add(Note("Perfil personalizado (" + m.Describe() + "): os eixos se editam na tela completa; aqui nada dele é perdido."));
        }

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
            Choice("Tela completa…", false, OpenMachineProfiles, "Pivôs, limites dos ângulos, modo de saída e excluir perfil: Mais → Perfil 4/5 eixos.")));
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

    /// <summary>Formulário curto do perfil (G2b, estado 5, sem "Cursos"): nome, controle, eixos, rotação máxima.</summary>
    private void MachineForm(StackPanel card)
    {
        bool creating = _machineEdit == -1;
        card.Children.Add(CardTitle(creating ? "Criar perfil" : "Editar perfil"));
        TextBox Text(string label, string value, string watermark, Action<string> set, string? emptyMark)
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
            return box;
        }

        Text("NOME", _mName, "digite o nome", v => _mName = v, "Falta o nome do perfil.");
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
        card.Children.Add(Note(_mAxes == CamJobInfo.MachineAxesCustom
            ? "Personalizado: salvar aqui não muda os eixos. Para editá-los, use a tela completa."
            : "Os botões carregam o modelo pronto; pivôs e limites se ajustam na tela completa."));
        Text("ROTAÇÃO MÁX.", _mRpm, "digite (rpm)", v => _mRpm = v, null);
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

    /// <summary>Escolha de botão no formulário: guarda e redesenha só o cartão (o texto digitado fica no rascunho).</summary>
    private void PickForm(Action change)
    {
        change();
        MachineCard(PrepMachine);
    }

    /// <summary>Salva o perfil com a mesma validação da tela completa, no config.json; o perfil criado ou editado fica ativo.</summary>
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

    // ---------------------------------------------------------------- campos do bruto

    /// <summary>
    /// Campo numérico da Preparação, com rótulo em cima. Vazio = null (nada suposto). <paramref name="needed"/>: o campo vazio conta
    /// (tracejado). <paramref name="invalid"/>: motivo do valor atual ser inválido (tracejado com o motivo).
    /// </summary>
    private StackPanel PrepNum(string label, Func<double?> get, Func<bool> needed, Func<string?> invalid, Action<double?> set, string watermark = "digite (mm)")
    {
        double? start = get();
        var box = new TextBox { Text = start is { } v0 ? Fmt(v0) : "", MinHeight = 26, Padding = new Thickness(6, 3), Watermark = watermark, FontSize = 12 };
        (Grid field, Action<string?> mark) = CamShell.DashedField(box);
        string? typedBad = null;
        void Remark()
        {
            string? why = typedBad
                ?? invalid()
                ?? (get() is null && needed() ? $"Falta {label}: digite um valor. Sem ele, o bruto fica incompleto e Gerar e Enviar ficam travados." : null);
            mark(why);
        }

        void Commit()
        {
            string typed = (box.Text ?? "").Trim();
            double? v = CamJobStock.ParseTyped(typed);
            typedBad = typed.Length > 0 && v is null ? $"\"{typed}\" não é um número. Falta {label}." : null;
            if (v != get())
            {
                set(v);
                AfterPrepEdit();
            }
            else
            {
                Remark();
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
        _prepMarks.Add(Remark);
        Remark();
        var p = new StackPanel { Spacing = 2 };
        p.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Muted });
        p.Children.Add(field);
        return p;
    }

    /// <summary>Depois de mudar um campo: refaz programa, árvore e barra (sem refazer os cartões, para não perder o foco) e as marcas.</summary>
    private void AfterPrepEdit()
    {
        Refresh(keep: true, props: false);
        foreach (Action remark in _prepMarks)
        {
            remark();
        }

        UpdateStockSummary();
    }

    /// <summary>Grade de campos em <paramref name="columns"/> colunas.</summary>
    private static Grid FieldGrid(int columns, IReadOnlyList<Control> fields)
    {
        var g = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        for (int c = 0; c < columns; c++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (int r = 0; r < (fields.Count + columns - 1) / columns; r++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int i = 0; i < fields.Count; i++)
        {
            Grid.SetColumn(fields[i], i % columns);
            Grid.SetRow(fields[i], i / columns);
            g.Children.Add(fields[i]);
        }

        return g;
    }

    // ---------------------------------------------------------------- Peça pronta

    /// <summary>Peça pronta: X e Y do desenho (caixa de todos os contornos), altura Z digitada. STEP da peça: em breve.</summary>
    private void PartCard(StackPanel card)
    {
        card.Children.Clear();
        card.Children.Add(CardTitle("Peça pronta"));
        card.Children.Add(Choices(Choice("Desenho DXF", true, null), Choice("STEP da peça · em breve", false, null, "Em breve: a peça em STEP fica hoje na aba Modelo 3D (Mais).")));
        (double X, double Y, double Width, double Length)? box = CamJobInfo.DrawingBox(_job.Contours);
        card.Children.Add(Note(box is { } b
            ? $"Largura X e profundidade Y vêm do desenho: {Fmt(b.Width)} × {Fmt(b.Length)} mm."
            : "Sem desenho: abra um DXF (faixa Arquivo) ou desenhe uma forma (item Peça pronta + bruto, aba Operações)."));
        card.Children.Add(PrepNum(CamJobStock.PartHeightField, () => _job.PartHeight, () => _job.StockShape is not null,
            () => _job.PartHeight is { } h ? CamJobStock.PartHeightError(h) : null, v => _job.PartHeight = v));
        card.Children.Add(Note(_job.StockShape is null
            ? "Um DXF não informa a altura. Só o bruto por sobra, cilindro e tubo usam."
            : "Um DXF não informa a altura. Sem ela, o bruto fica incompleto e Gerar e Enviar ficam travados."));
    }

    // ---------------------------------------------------------------- Bruto

    /// <summary>Troca o formato do bruto (o outro formato some: uma fonte só) e refaz os cartões.</summary>
    private void SetStockKind(StockShapeKind? kind)
    {
        // guarda o formato que sai (só na tela), para voltar a ele com os mesmos valores
        if (_job.StockShape is { } leaving)
        {
            _lastShapes[leaving.Kind] = leaving;
            if (leaving.Kind == StockShapeKind.Margins)
            {
                _lastBlockAbsolute = false;
            }
        }
        else if (_job.Stock is { } leavingAbs)
        {
            _lastAbsolute = leavingAbs;
            _lastBlockAbsolute = true;
        }
        else if (_absoluteMode)
        {
            _lastBlockAbsolute = true;
        }

        _absoluteMode = kind is null;
        if (kind is { } k)
        {
            if (_job.StockShape?.Kind != k)
            {
                StockAxis axis = _job.StockShape?.Axis ?? StockAxis.Z;
                _job.SetStock(null, _lastShapes.TryGetValue(k, out JobStockShape? before) ? before : new JobStockShape(k) { Axis = axis });
            }
        }
        else if (_job.StockShape is not null)
        {
            // bloco absoluto: volta o último desta sessão; sem ele, começa vazio, com as medidas do desenho para conferir
            _job.SetStock(_lastAbsolute, null);
        }

        Refresh(keep: true);
    }

    /// <summary>
    /// Cartão Bruto: formato (Bloco, Cilindro, Tubo; STEP em breve), no bloco o modo (sobra por lado ou absoluto), os campos do formato,
    /// a posição (deslocamento da posição centrada) e o resumo com a vista de cima.
    /// </summary>
    private void StockCard(StackPanel card)
    {
        card.Children.Clear();
        _prepMarks.Clear();
        TextBlock title = CardTitle("Bruto");
        ToolTip.SetTip(title, "Você escolhe o formato do bruto, que pode ser diferente da peça. O bruto não entra no G-code: serve ao 3D e à conferência.");
        card.Children.Add(title);
        JobStockShape? shape = _job.StockShape;
        bool block = shape is null or { Kind: StockShapeKind.Margins };
        bool defined = shape is not null || _job.Stock is not null || _absoluteMode;
        card.Children.Add(Choices(
            Choice("Bloco", defined && block, () => SetStockKind(defined && block
                ? (_job.Stock is null && !_absoluteMode ? StockShapeKind.Margins : null)
                : (_lastBlockAbsolute ? null : StockShapeKind.Margins))),
            Choice("Cilindro", shape?.Kind == StockShapeKind.Cylinder, () => SetStockKind(StockShapeKind.Cylinder)),
            Choice("Tubo", shape?.Kind == StockShapeKind.Tube, () => SetStockKind(StockShapeKind.Tube)),
            Choice("Importar STEP", shape?.Kind == StockShapeKind.Step, () => SetStockKind(StockShapeKind.Step), "Bruto fundido ou forjado em STEP.")));
        if (!defined)
        {
            card.Children.Add(UiKit.StatusNote("Bruto não definido. Escolha o formato acima.", "pending"));
            return;
        }

        if (block)
        {
            card.Children.Add(Choices(
                Choice("Sobra por lado", shape is not null, () => SetStockKind(StockShapeKind.Margins)),
                Choice("Bloco absoluto", shape is null, () => SetStockKind(null))));
        }

        if (shape is null)
        {
            AbsoluteBlock(card);
            return;
        }

        if (shape.Kind == StockShapeKind.Step)
        {
            StepStock(card);
            return;
        }

        bool Need() => true;
        Control F(string field) => PrepNum(field, () => _job.StockShape?.Fields().FirstOrDefault(f => f.Name == field).Value, Need,
            () => _job.StockShape?.InvalidReason(field), v => _job.StockShape = _job.StockShape?.With(field, v));

        if (shape.Kind == StockShapeKind.Margins)
        {
            card.Children.Add(Caps("BLOCO: QUANTO SOBRA EM CADA LADO"));
            card.Children.Add(FieldGrid(3, [F(JobStockShape.XMinusField), F(JobStockShape.XPlusField), F(JobStockShape.YMinusField),
                F(JobStockShape.YPlusField), F(JobStockShape.ZTopField), F(JobStockShape.ZBottomField)]));
            card.Children.Add(Note("Sobras a partir da peça pronta. Z0 no topo da peça; a sobra em cima fica acima dele. Trocar o desenho recalcula com as mesmas sobras."));
        }
        else
        {
            card.Children.Add(Caps("EIXO"));
            card.Children.Add(Choices([.. Enum.GetValues<StockAxis>().Select(a => (Control)Choice(a.ToString(), shape.Axis == a, () =>
            {
                _job.StockShape = _job.StockShape! with { Axis = a };
                Refresh(keep: true);
            }))]));
            card.Children.Add(shape.Kind == StockShapeKind.Cylinder
                ? FieldGrid(2, [F(JobStockShape.DiameterField), F(JobStockShape.LengthField)])
                : FieldGrid(3, [F(JobStockShape.OuterField), F(JobStockShape.InnerField), F(JobStockShape.LengthField)]));
        }

        card.Children.Add(Caps("POSIÇÃO: DESLOCAMENTO (0 = CENTRADO NA PEÇA)"));
        card.Children.Add(FieldGrid(3, [F(JobStockShape.OffsetXField), F(JobStockShape.OffsetYField), F(JobStockShape.OffsetZField)]));
        _stockSummary = new ContentControl();
        card.Children.Add(_stockSummary);
        UpdateStockSummary();
    }

    /// <summary>Resumo do bruto: completo (medidas + vista de cima) ou incompleto (o motivo). Só visualização.</summary>
    private void UpdateStockSummary()
    {
        if (_stockSummary is null)
        {
            return;
        }

        StockCheck c = CamJobStock.Check(_job);
        if (c.Solid is not { } s)
        {
            _stockSummary.Content = CamJobStock.IncompleteReason(c) is { } why
                ? UiKit.StatusNote("Bruto incompleto: " + why + ". Não aparece no 3D; Gerar e Enviar ficam travados até completar (Salvar continua livre).", "pending")
                : null;
            return;
        }

        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        Control top = TopView(s);
        DockPanel.SetDock(top, Dock.Left);
        row.Children.Add(top);
        row.Children.Add(new TextBlock
        {
            Text = "Bruto resultante: " + CamJobStock.Title(_job) + $" (X {Fmt(s.MinX)} a {Fmt(s.MaxX)} · Y {Fmt(s.MinY)} a {Fmt(s.MaxY)} · Z {Fmt(s.MinZ)} a {Fmt(s.MaxZ)}). Só visualização.",
            FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        });
        _stockSummary.Content = row;
    }

    /// <summary>Vista de cima (XY): a peça (caixa do desenho) dentro do bruto, em escala. Só visualização.</summary>
    private Control TopView(StockSolid s)
    {
        const double w = 120, h = 64, pad = 5;
        var canvas = new Canvas { Width = w, Height = h, Background = Brush.Parse("#0d1117") };
        (double X, double Y, double Width, double Length)? part = CamJobInfo.DrawingBox(_job.Contours);
        double x0 = Math.Min(s.MinX, part?.X ?? s.MinX), x1 = Math.Max(s.MaxX, part is { } p1 ? p1.X + p1.Width : s.MaxX);
        double y0 = Math.Min(s.MinY, part?.Y ?? s.MinY), y1 = Math.Max(s.MaxY, part is { } p2 ? p2.Y + p2.Length : s.MaxY);
        double k = Math.Min((w - 2 * pad) / Math.Max(x1 - x0, 1e-6), (h - 2 * pad) / Math.Max(y1 - y0, 1e-6));
        double ox = (w - (x1 - x0) * k) / 2, oy = (h - (y1 - y0) * k) / 2;
        void Place(Shape shape, double ax, double ay, double bx, double by)
        {
            shape.Width = Math.Max((bx - ax) * k, 1);
            shape.Height = Math.Max((by - ay) * k, 1);
            Canvas.SetLeft(shape, ox + (ax - x0) * k);
            Canvas.SetTop(shape, oy + (y1 - by) * k); // Y para cima
            canvas.Children.Add(shape);
        }

        if (part is { } p)
        {
            Place(new Rectangle { Fill = Brush.Parse("#c06a1c"), Opacity = 0.85 }, p.X, p.Y, p.X + p.Width, p.Y + p.Length);
        }

        Shape outline = s.Kind != StockSolidKind.Box && s.Axis == StockAxis.Z ? new Ellipse() : new Rectangle();
        outline.Stroke = Brush.Parse("#58a6ff");
        outline.StrokeThickness = 1.2;
        outline.StrokeDashArray = [4, 3];
        Place(outline, s.MinX, s.MinY, s.MaxX, s.MaxY);
        ToolTip.SetTip(canvas, "Vista de cima: a peça (laranja) dentro do bruto (azul tracejado). Só visualização.");
        return canvas;
    }

    /// <summary>
    /// Bloco absoluto (o bruto de antes; arquivo antigo abre assim): canto X e Y, comprimento, largura e altura, com o topo em Z0.
    /// Vale só ao clicar em "Definir bruto"; campo vazio ou inválido fica tracejado e não muda nada (nada suposto).
    /// </summary>
    private void AbsoluteBlock(StackPanel card)
    {
        JobStock? st = _job.Stock;
        (double X, double Y, double Width, double Length)? box = st is { } s0 ? (s0.X, s0.Y, s0.Width, s0.Length) : CamJobInfo.DrawingBox(_job.Contours);
        if (st is null)
        {
            card.Children.Add(UiKit.StatusNote(box is null
                ? "Bloco absoluto não definido. Digite as medidas e clique em Definir bruto."
                : "Bloco absoluto não definido. Medidas preenchidas pelo desenho: confira, digite a altura e clique em Definir bruto.", "pending"));
        }
        else
        {
            card.Children.Add(Note("Trabalho com bloco absoluto (valores do arquivo, nada mudado). Passar para \"Sobra por lado\" pede a altura da peça. O G-code continua igual."));
        }

        var marks = new List<(TextBox Box, Action<string?> Mark)>();
        TextBox Field(string label, double? value, out Control control)
        {
            var b = new TextBox { Text = value is { } v ? Fmt(v) : "", MinHeight = 28, Watermark = "digite (mm)", FontSize = 12 };
            (Grid field, Action<string?> mark) = CamShell.DashedField(b);
            marks.Add((b, mark));
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Muted });
            p.Children.Add(field);
            control = p;
            return b;
        }

        TextBox x = Field("Canto X", box?.X ?? 0, out Control cx);
        TextBox y = Field("Canto Y", box?.Y ?? 0, out Control cy);
        TextBox width = Field("Comprimento (X)", box?.Width, out Control cw);
        TextBox length = Field("Largura (Y)", box?.Length, out Control cl);
        TextBox height = Field("Altura (Z)", st?.Thickness, out Control ch);
        card.Children.Add(FieldGrid(3, [cx, cy, cw, cl, ch]));
        card.Children.Add(Note("Topo do bruto em Z0 (desce até Z −altura). O Z topo de cada operação continua nas propriedades dela."));
        foreach ((TextBox b, Action<string?> mark) in marks)
        {
            if (string.IsNullOrWhiteSpace(b.Text))
            {
                mark("Falta este valor: digite e clique em Definir bruto.");
            }
        }

        var error = new ContentControl();
        var apply = new Button { Content = "Definir bruto", Padding = new Thickness(12, 5), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        apply.Click += (_, _) =>
        {
            double w = ParseNum(width.Text), l = ParseNum(length.Text), hh = ParseNum(height.Text), px = ParseNum(x.Text), py = ParseNum(y.Text);
            foreach ((TextBox b, Action<string?> mark) in marks)
            {
                mark(double.IsNaN(ParseNum(b.Text)) ? "Falta este valor (vazio ou não é número)." : null);
            }

            string? problem = CamJobInfo.StockError(w, l, hh) ?? (double.IsNaN(px) || double.IsNaN(py) ? "Confira o canto X e Y do bruto." : null);
            if (problem is not null)
            {
                error.Content = UiKit.StatusNote("✕ inválido: " + problem, "bad");
                return;
            }

            _job.SetStock(new JobStock(px, py, w, l, hh), null);
            Refresh(keep: true);
        };
        card.Children.Add(apply);
        card.Children.Add(error);
    }

    // ---------------------------------------------------------------- bruto em STEP (PR 2)

    /// <summary>Escolhe um STEP para o bruto e devolve o caminho no computador (null = cancelou ou o arquivo não tem caminho local).</summary>
    public Func<Task<string?>>? PickStockStep { get; set; }

    /// <summary>Malha do STEP do bruto lida nesta sessão (caminho e malha nas coordenadas do arquivo). Nunca vai para o arquivo.</summary>
    private (string Path, TriangleMesh Mesh)? _stepMesh;

    /// <summary>Leitura do STEP em andamento (os botões não disparam outra).</summary>
    private bool _stepReading;

    /// <summary>
    /// Cartão do bruto STEP: arquivo (nome, trocar), motivo e "Localizar de novo" se sumiu, resumo (caixa e volume), posição
    /// ("Como está no STEP" ou "Digitar X Y Z") e o botão que lê o STEP para o 3D. Abrir o trabalho nunca lê o arquivo.
    /// </summary>
    private void StepStock(StackPanel card)
    {
        JobStockShape shape = _job.StockShape!;
        string? name = CamJobStock.StepFileName(_job);
        var file = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var change = new Button { Content = name is null ? "Escolher arquivo STEP…" : "trocar", Padding = new Thickness(8, 2), FontSize = 11, IsEnabled = !_stepReading };
        change.Click += async (_, _) => await ImportStockStep();
        DockPanel.SetDock(change, Dock.Right);
        file.Children.Add(change);
        file.Children.Add(new TextBlock
        {
            Text = name ?? "Nenhum arquivo escolhido", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = name is null ? Muted : Brushes.White,
        });
        if (shape.StepPath is { } path)
        {
            ToolTip.SetTip(file, path);
        }

        card.Children.Add(file);
        if (shape.StepFileProblem is not null)
        {
            // o motivo completo fica no resumo, abaixo; aqui só o que fazer
            card.Children.Add(UiKit.StatusNote("O arquivo sumiu ou mudou de lugar. Não usa um bruto suposto: localize o STEP de novo.", "pending"));
            var locate = new Button { Content = "Localizar de novo", Padding = new Thickness(10, 4), FontSize = 12, IsEnabled = !_stepReading };
            locate.Click += async (_, _) => await ImportStockStep();
            card.Children.Add(locate);
        }
        else if (shape.StepSummary is { } b)
        {
            card.Children.Add(Note($"Resumo lido do arquivo: caixa {Fmt(b.SizeX)} × {Fmt(b.SizeY)} × {Fmt(b.SizeZ)} mm, volume {CamJobStock.VolumeText(b.Volume)}. O trabalho guarda o caminho e o resumo; o STEP só é lido para o 3D."));
        }
        else
        {
            card.Children.Add(UiKit.StatusNote("Escolha o arquivo STEP do bruto (fundido ou forjado). Sem ele, Gerar e Enviar ficam travados.", "pending"));
        }

        card.Children.Add(Caps("POSIÇÃO"));
        void Place(StepPlacement p)
        {
            JobStockShape s = _job.StockShape!;
            // "Digitar X Y Z" começa em 0 (a mesma posição do arquivo)
            _job.StockShape = p == StepPlacement.Typed && s.Placement != p
                ? s with { Placement = p, OffsetX = s.OffsetX ?? 0, OffsetY = s.OffsetY ?? 0, OffsetZ = s.OffsetZ ?? 0 }
                : s with { Placement = p };
            Refresh(keep: true);
        }

        card.Children.Add(Choices(
            Choice("Como está no STEP", shape.Placement == StepPlacement.AsInFile, () => Place(StepPlacement.AsInFile), "As coordenadas do STEP valem como as do programa (Z0 do programa = Z0 do STEP)."),
            Choice("Digitar X Y Z", shape.Placement == StepPlacement.Typed, () => Place(StepPlacement.Typed), "Deslocamento somado às coordenadas do STEP.")));
        if (shape.Placement == StepPlacement.Typed)
        {
            Control F(string field) => PrepNum(field, () => _job.StockShape?.Fields().FirstOrDefault(f => f.Name == field).Value, () => true,
                () => null, v => _job.StockShape = _job.StockShape?.With(field, v));
            card.Children.Add(FieldGrid(3, [F(JobStockShape.OffsetXField), F(JobStockShape.OffsetYField), F(JobStockShape.OffsetZField)]));
        }

        card.Children.Add(Note("O STEP e o DXF não têm sistema de coordenadas garantido em comum: confira a caixa do bruto contra a peça. Nada é alinhado sozinho."));
        _stockSummary = new ContentControl();
        card.Children.Add(_stockSummary);
        UpdateStockSummary();
        if (CamJobStock.Check(_job).Complete && _stepMesh?.Path != shape.StepPath)
        {
            var show = new Button { Content = _stepReading ? "Lendo o STEP…" : "Mostrar o sólido no 3D (lê o STEP)", Padding = new Thickness(10, 4), FontSize = 12, IsEnabled = !_stepReading };
            ToolTip.SetTip(show, "Até ler, o 3D mostra só a caixa do resumo. A leitura não trava a tela.");
            show.Click += async (_, _) => await LoadStockStepMesh();
            card.Children.Add(show);
        }

        card.Children.Add(Note("O bruto não entra no G-code. O simulador do Code não desenha bruto STEP."));
    }

    /// <summary>Lê o STEP fora da tela (não trava). Devolve a malha ou o erro, com o tempo de leitura.</summary>
    private static async Task<(TriangleMesh? Mesh, string? Error, string? Notice, double Seconds)> ReadStockStep(string path)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            StepResult r = await Task.Run(() => StepMeshReader.Read(File.ReadAllBytes(path)));
            return (r.Mesh, r.Error, r.Notice, clock.Elapsed.TotalSeconds);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, "não foi possível ler o arquivo (" + e.Message + ")", null, clock.Elapsed.TotalSeconds);
        }
    }

    /// <summary>Importa, troca ou localiza de novo o STEP do bruto: lê o arquivo para o resumo e guarda a malha para o 3D.</summary>
    private async Task ImportStockStep()
    {
        if (_stepReading || PickStockStep is null || await PickStockStep() is not { } path)
        {
            return;
        }

        _stepReading = true;
        Refresh(keep: true);
        Errors.Text = "• Lendo o STEP do bruto… (a tela continua livre)";
        (TriangleMesh? mesh, string? error, string? notice, double seconds) = await ReadStockStep(path);
        _stepReading = false;
        if (mesh is null)
        {
            Refresh(keep: true);
            Errors.Text = "• STEP do bruto não lido: " + error;
            return;
        }

        // o usuário pode ter trocado o formato enquanto lia: aí o STEP lido não vale
        if (_job.StockShape is not { Kind: StockShapeKind.Step } current)
        {
            Refresh(keep: true);
            Errors.Text = "";
            return;
        }

        _stepMesh = (path, mesh);
        _job.StockShape = CamJobStock.ImportStep(current, path, mesh);
        Refresh(keep: true);
        Errors.Text = $"• STEP do bruto lido em {seconds.ToString("0.0", Br)} s." + (notice is { } n ? "\n• " + n : "");
    }

    /// <summary>
    /// Lê o STEP já escolhido só para mostrar o sólido no 3D (sob demanda). Se o arquivo mudou desde a importação (caixa diferente),
    /// não mostra e pede para trocar, para o 3D não desenhar um bruto diferente do resumo.
    /// </summary>
    private async Task LoadStockStepMesh()
    {
        if (_stepReading || _job.StockShape is not { Kind: StockShapeKind.Step, StepPath: { } path, StepSummary: { } summary })
        {
            return;
        }

        _stepReading = true;
        Refresh(keep: true);
        (TriangleMesh? mesh, string? error, _, double seconds) = await ReadStockStep(path);
        _stepReading = false;
        string message;
        if (mesh is null)
        {
            message = "• STEP do bruto não lido: " + error;
        }
        else if (!SameBox(StepStockSummary.From(mesh), summary))
        {
            message = "• O STEP mudou desde a importação (a caixa não bate com o resumo). Use \"trocar\" para ler de novo e atualizar o resumo.";
        }
        else
        {
            if (_job.StockShape?.StepPath == path)
            {
                _stepMesh = (path, mesh);
            }

            message = $"• STEP do bruto lido em {seconds.ToString("0.0", Br)} s.";
        }

        Refresh(keep: true);
        Errors.Text = message;
    }

    private static bool SameBox(StepStockSummary a, StepStockSummary b) =>
        new[] { a.MinX - b.MinX, a.MinY - b.MinY, a.MinZ - b.MinZ, a.MaxX - b.MaxX, a.MaxY - b.MaxY, a.MaxZ - b.MaxZ }.All(d => Math.Abs(d) < 1e-3);

    /// <summary>Bruto para o 3D: o resolvido do Core e, no STEP já lido nesta sessão, o sólido na posição do programa.</summary>
    private StockSolid? StockForScene()
    {
        StockSolid? solid = CamJobStock.Check(_job).Solid;
        return solid is { Kind: StockSolidKind.Step } && _job.StockShape is { } shape && _stepMesh is { } m && m.Path == shape.StepPath
            ? solid with { Mesh = CamJobStock.PlaceStepMesh(shape, m.Mesh) }
            : solid;
    }
}
