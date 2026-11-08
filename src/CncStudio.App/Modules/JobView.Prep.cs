using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Cartões Máquina (só leitura), Peça pronta e Bruto da aba Preparação (UX-04f). O bruto e a altura da peça nunca entram no G-code;
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

    // ---------------------------------------------------------------- Máquina (só leitura no PR 1)

    /// <summary>Perfil atual: nome, controle e eixos (ou "personalizado"); trocar e criar perfil vêm no PR 2.</summary>
    private void MachineCard(StackPanel card)
    {
        card.Children.Clear();
        card.Children.Add(CardTitle("Máquina"));
        MachineProfile? m = MachineSource?.Invoke();
        if (m is null)
        {
            card.Children.Add(UiKit.StatusNote("Nenhum perfil ativo. Sem perfil, o CAM gera em 3 eixos; 4º eixo e texto no cilindro pedem perfil.", "pending"));
        }
        else
        {
            card.Children.Add(Caps("PERFIL"));
            card.Children.Add(new TextBlock { Text = m.Name, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(Caps("CONTROLE"));
            card.Children.Add(Choices(Choice("Fanuc", m.Controller == CamDialect.Fanuc, null), Choice("Siemens", m.Controller == CamDialect.Siemens, null)));
            card.Children.Add(Note("O controle do trabalho (na linha de baixo) é o que vale para o programa."));
            if (m.Controller != _job.Dialect)
            {
                card.Children.Add(UiKit.StatusNote($"O trabalho está em {(_job.Dialect == CamDialect.Siemens ? "Siemens" : "Fanuc")} e o perfil em {(m.Controller == CamDialect.Siemens ? "Siemens" : "Fanuc")}. Confira antes de rodar.", "pending"));
            }

            card.Children.Add(Caps("EIXOS"));
            string axes = CamJobInfo.MachineAxesLabel(m);
            var buttons = CamJobInfo.MachineAxesOptions.Select(o => (Control)Choice(o, o == axes, null)).ToList();
            if (axes == CamJobInfo.MachineAxesCustom)
            {
                buttons.Add(Choice("Personalizado", true, null, m.Describe()));
            }

            card.Children.Add(Choices([.. buttons]));
            card.Children.Add(Caps("ROTAÇÃO MÁX."));
            card.Children.Add(new TextBlock
            {
                Text = m.MaxSpindleRpm is { } rpm ? rpm.ToString("0", Br) + " rpm" : "não informada (limite do fuso não verificado)",
                FontSize = 13, Foreground = m.MaxSpindleRpm is null ? Muted : Brushes.White,
            });
        }

        card.Children.Add(Choices(Choice("Trocar e criar perfil · em breve", false, null, "Hoje o perfil é escolhido no Mais → Perfil 4/5 eixos.")));
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
            Choice("Importar STEP · em breve", false, null, "Em breve (próxima etapa): bruto fundido ou forjado em STEP.")));
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
}
