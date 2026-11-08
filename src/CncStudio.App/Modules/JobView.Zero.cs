using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>Cartão Zero da fixação 1 na Preparação (UX-04g PR B, guia G3).</summary>
public partial class JobView
{
    /// <summary>
    /// Zero da fixação 1 (UX-04g PR B, guia G3): G5x em leitura, X e Y num dos 9 pontos da peça pronta ou na origem do desenho, Z0 no topo
    /// ou na base. Vazio: tracejado laranja e o motivo; trava Gerar e Enviar, nunca Salvar. Com face girada, texto no cilindro ou 3+2 só
    /// vale a origem do desenho. Arquivo antigo mostra "Origem do desenho (como antes)".
    /// </summary>
    private void ZeroProps()
    {
        var head = new DockPanel();
        var g5x = new TextBlock { Text = "G5x da fixação: " + CamJobInfo.WorkOffsetName(_job), FontSize = 11, Foreground = Brush.Parse("#9aa7b5"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(g5x, Dock.Right);
        head.Children.Add(g5x);
        head.Children.Add(new TextBlock { Text = "Zero da fixação 1", FontSize = 18, FontWeight = FontWeight.Bold });
        PropsPanel.Children.Add(head);
        PropsPanel.Children.Add(new TextBlock { Text = CamJobInfo.OriginTitle(_job), TextWrapping = TextWrapping.Wrap, FontSize = 12 });

        IReadOnlyList<string> problems = CamJobZero.Problems(_job);
        if (problems.Count > 0)
        {
            var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            note.Children.Add(CamShell.Badge("!", "pending"));
            note.Children.Add(new TextBlock
            {
                Text = "Incompleto: " + string.Join(" · ", problems) + ".", FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = UiKit.Status("pending").Fg, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center,
            });
            PropsPanel.Children.Add(note);
        }

        if (_job.Zero is { AsBefore: true })
        {
            // G3 estado 5: arquivo antigo; nada novo no arquivo até escolher outro zero
            PropsPanel.Children.Add(UiKit.StatusNote("Trabalho aberto de um arquivo antigo. O zero está como \"Origem do desenho (como antes)\": X0 Y0 do desenho, Z0 dos níveis digitados. O programa sai igual.", "pending"));
            PropsPanel.Children.Add(Choices(
                Choice("Origem do desenho", true, null),
                Choice("Escolher outro", false, () => SetZero(new JobZero(ZeroPoint.Drawing, ZeroLevel.Top)), "Passa a gravar o zero no arquivo (versão 3) e o programa ganha a linha do zero no cabeçalho.")));
            PropsPanel.Children.Add(Note("Salvar não acrescenta campo ao arquivo, a não ser que você escolha outro zero."));
        }
        else if (CamJobZero.OnlyDrawing(_job))
        {
            // G3 estado 4: os pivôs do perfil são medidos a partir do zero da peça; nada é trocado sozinho
            string why = CamJobZero.RotaryReason + " Neste trabalho: " + string.Join(", ", CamJobZero.RotaryUses(_job)) + ".";
            bool drawing = _job.Zero is { Point: ZeroPoint.Drawing, Level: ZeroLevel.Top };
            PropsPanel.Children.Add(Choices(
                Choice("Origem do desenho", drawing, () => SetZero(new JobZero(ZeroPoint.Drawing, ZeroLevel.Top)), "X0 Y0 do desenho, Z0 no topo."),
                Choice("Centro", false, null, why),
                Choice("Canto", false, null, why),
                Choice("Aresta", false, null, why)));
            PropsPanel.Children.Add(UiKit.StatusNote(why, "pending"));
            PropsPanel.Children.Add(Note("A opção volta quando essas operações saírem do trabalho."));
        }
        else
        {
            ZeroPicker();
        }

        if (_job.Zero is null)
        {
            PropsPanel.Children.Add(UiKit.StatusNote("Sem zero, o programa não é gerado nem enviado. Salvar continua livre.", "pending"));
        }
        else if (_job.Zero is { AsBefore: false } && problems.Count == 0)
        {
            PropsPanel.Children.Add(new Border
            {
                BorderBrush = Brush.Parse("#2a3442"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6), Margin = new Thickness(0, 8, 0, 0),
                Child = new TextBlock { Text = CamJobZero.Summary(_job), FontSize = 12, TextWrapping = TextWrapping.Wrap },
            });
        }

        PropsPanel.Children.Add(UiKit.StatusNote("Origem " + CamJobInfo.OriginState().Text + ": confira na máquina. Um zero por fixação vem em breve.", "pending"));
    }

    /// <summary>Grade dos 9 pontos, "Origem do desenho" e Z0 topo/base (G3 estados 1 a 3).</summary>
    private void ZeroPicker()
    {
        JobZero zero = _job.Zero ?? new JobZero(null, null);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*"), Margin = new Thickness(0, 6, 0, 0) };
        var left = new StackPanel();
        left.Children.Add(Caps("X E Y NA PEÇA PRONTA"));
        var cells = new UniformGrid { Rows = 3, Columns = 3, Width = 108, Height = 96 };
        foreach (ZeroPoint p in CamJobZero.Grid)
        {
            bool on = zero.Point == p;
            var dot = new Button
            {
                Width = 20, Height = 20, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1.5),
                Background = on ? Accent : Brushes.Transparent, BorderBrush = on ? Accent : Brush.Parse("#5a6675"),
            };
            string name = CamJobZero.PointName(p);
            ToolTip.SetTip(dot, char.ToUpperInvariant(name[0]) + name[1..] + " da peça pronta");
            ZeroPoint pick = p;
            dot.Click += (_, _) => SetZero(zero with { Point = pick });
            cells.Children.Add(dot);
        }

        var box = new Border
        {
            BorderBrush = Brush.Parse("#2a3442"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4), Child = cells, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
        };
        (Grid boxField, Action<string?> boxMark) = CamShell.DashedField(box);
        boxMark(zero.Point is null ? "Falta o X e Y do zero. Sem ele, Gerar e Enviar ficam travados." : null);
        left.Children.Add(boxField);
        row.Children.Add(left);

        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        right.Children.Add(Caps("OU"));
        right.Children.Add(Choices(Choice("Origem do desenho", zero.Point == ZeroPoint.Drawing, () => SetZero(zero with { Point = ZeroPoint.Drawing }), "X0 Y0 do DXF, como antes.")));
        right.Children.Add(Caps("Z0"));
        WrapPanel levels = Choices(
            Choice("Topo", zero.Level == ZeroLevel.Top, () => SetZero(zero with { Level = ZeroLevel.Top }), "Z0 no topo da peça pronta."),
            Choice("Base", zero.Level == ZeroLevel.Bottom, () => SetZero(zero with { Level = ZeroLevel.Bottom }), "Z0 na base da peça pronta (precisa da altura da peça)."));
        levels.HorizontalAlignment = HorizontalAlignment.Left;
        (Grid levelField, Action<string?> levelMark) = CamShell.DashedField(levels);
        levelMark(zero.Level is null ? "Falta o Z0 (topo ou base). Sem ele, Gerar e Enviar ficam travados." : null);
        right.Children.Add(levelField);
        row.Children.Add(right);
        PropsPanel.Children.Add(row);
        PropsPanel.Children.Add(Note("9 pontos: 4 cantos, 4 meios de aresta e o centro da caixa da peça pronta."));

        if (zero.Level == ZeroLevel.Bottom)
        {
            // G3 estado 3: o Z digitado continua medido do topo; o programa sai deslocado pela altura
            PropsPanel.Children.Add(UiKit.StatusNote("As profundidades digitadas continuam medidas do topo da peça pronta. Com Z0 na base, o programa sai com Z positivo (ex.: profundidade 5 numa peça de 20 sai em Z15).", "pending"));
            if (_job.PartHeight is null)
            {
                PropsPanel.Children.Add(Note("A altura da peça é necessária aqui. Sem ela o zero fica incompleto."));
                PropsPanel.Children.Add(Choices(Choice("Digitar a altura no cartão Peça pronta", false, () => PrepPart.BringIntoView())));
            }
        }
    }

    /// <summary>Troca o zero da peça (escolha do usuário) e refaz o programa e as setas do 3D.</summary>
    private void SetZero(JobZero zero)
    {
        _job.Zero = zero.Point is null && zero.Level is null ? null : zero;
        Refresh(keep: true);
    }
}
