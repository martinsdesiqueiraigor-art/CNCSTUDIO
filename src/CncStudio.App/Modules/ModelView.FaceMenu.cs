using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.Core.Cam3D;

namespace CncStudio.App.Modules;

/// <summary>
/// Menu da face (UX-03b): ao escolher uma face ou aresta na vista 3D, abre perto do clique um menu com o tipo e a cota,
/// só as operações que servem e "Ver todas as operações…" (as que não servem, apagadas, com o motivo). A regra vem do
/// Core (<see cref="FaceMenuRules"/>). A operação continua na aba Modelo 3D (não entra na árvore do Trabalho) e nada é
/// preenchido: o menu leva ao campo do fundo, que o usuário digita. Teclado: setas, Enter e Esc.
/// </summary>
public sealed partial class ModelView
{
    private const double MenuWidth = 290;

    private readonly Canvas _menuLayer = new() { ClipToBounds = true };
    private readonly TextBlock _originInfo = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsVisible = false, Foreground = new SolidColorBrush(Color.Parse("#FF9F1C")) };
    private Border? _menu;
    private ScrollViewer? _menuScroll;
    private Point _menuAt;
    private int? _originFace;

    private void CloseFaceMenu()
    {
        if (_menu is not null)
        {
            _menuLayer.Children.Remove(_menu);
            _menu = null;
        }
    }

    private void ShowFaceMenu(FaceMenu menu, Point at, int? faceId)
    {
        CloseFaceMenu();
        _menuAt = at;
        var items = new StackPanel { Spacing = 2 };
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = menu.Header, FontWeight = FontWeight.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Lido da malha, não da face do CAD.", FontSize = 11, Opacity = 0.6 });
        var scroll = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        body.Children.Add(scroll);
        _menuScroll = scroll;
        var all = new Button { Content = "Ver todas as operações…", Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        AutomationName(all, "Ver todas as operações");
        body.Children.Add(all);

        var border = new Border
        {
            Width = MenuWidth,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 4 14 0 #66000000"),
            Child = body,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("CardBackground"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("SubtleBorder"));
        border.AddHandler(KeyDownEvent, OnMenuKey, RoutingStrategies.Tunnel);

        void Fill(bool showAll)
        {
            items.Children.Clear();
            foreach (FaceMenuItem item in showAll ? menu.All : menu.Fits)
            {
                items.Children.Add(MenuRow(item, menu.Header, faceId));
            }

            // Sem nada que sirva: só o cabeçalho e "Ver todas" (nunca menu vazio)
            items.IsVisible = items.Children.Count > 0;
            all.IsVisible = !showAll;
        }

        all.Click += (_, _) =>
        {
            Fill(true);
            Dispatcher.UIThread.Post(() =>
            {
                PlaceMenu();
                FocusFirst();
            }, DispatcherPriority.Loaded);
        };
        Fill(false);
        _menu = border;
        _menuLayer.Children.Add(border);
        PlaceMenu();
        Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
    }

    /// <summary>Linha do menu: item que serve = botão; item que não serve = apagado, com o motivo escrito (sem precisar passar o mouse).</summary>
    private Control MenuRow(FaceMenuItem item, string header, int? faceId)
    {
        var row = new DockPanel();
        Control icon = UiKit.Icon(item.Item.Icon, "#8FB4FF", 18);
        icon.Margin = new Thickness(0, 1, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = item.Item.Name, FontSize = 12 });
        text.Children.Add(new TextBlock { Text = item.Unavailable ?? item.Item.Hint, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        if (!item.Fits)
        {
            return new Border { Child = row, Padding = new Thickness(8, 4), Opacity = 0.5 };
        }

        var button = new Button { Content = row, Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        AutomationName(button, item.Item.Name);
        button.Click += (_, _) => ApplyFaceMenu(item, header, faceId);
        return button;
    }

    private static void AutomationName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    /// <summary>Leva à seção da operação, presa à face clicada; o fundo fica vazio para o usuário digitar (nada é suposto).</summary>
    private void ApplyFaceMenu(FaceMenuItem item, string header, int? faceId)
    {
        CloseFaceMenu();
        (string name, string field) = item.Item.Id == "furacao" ? ("Furação", "DZ") : ("Bolsão", "PZB");
        _originFace = faceId;
        _originInfo.Text = $"{name} · origem: face selecionada ({header}). Digite o Z do fundo e confira os campos; nada é suposto.";
        _originInfo.IsVisible = true;
        TextBox box = _f[field];
        box.BringIntoView();
        box.Focus();
    }

    /// <summary>Põe o menu perto do clique, dentro da vista, sem passar de 60% da altura (não cobre a peça inteira).</summary>
    private void PlaceMenu()
    {
        if (_menu is null)
        {
            return;
        }

        Size view = _menuLayer.Bounds.Size;
        if (_menuScroll is not null)
        {
            _menuScroll.MaxHeight = Math.Max(80, (view.Height * 0.6) - 90);
        }

        _menu.Measure(new Size(MenuWidth, double.PositiveInfinity));
        Size size = _menu.DesiredSize;
        double x = _menuAt.X + 14;
        if (x + size.Width > view.Width - 8)
        {
            x = _menuAt.X - size.Width - 14;
        }

        double y = Math.Min(_menuAt.Y + 8, view.Height - size.Height - 8);
        Canvas.SetLeft(_menu, Math.Max(8, x));
        Canvas.SetTop(_menu, Math.Max(8, y));
    }

    private List<Button> MenuButtons() =>
        _menu?.GetVisualDescendantsOf<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToList() ?? [];

    private void FocusFirst() => MenuButtons().FirstOrDefault()?.Focus(NavigationMethod.Directional);

    private void OnMenuKey(object? sender, KeyEventArgs e)
    {
        List<Button> buttons = MenuButtons();
        int at = buttons.FindIndex(b => b.IsFocused);
        switch (e.Key)
        {
            case Key.Escape:
                CloseFaceMenu();
                _gl.Focus();
                e.Handled = true;
                break;
            case Key.Down or Key.Up when buttons.Count > 0:
                int step = e.Key == Key.Down ? 1 : -1;
                int next = at < 0 ? 0 : (at + step + buttons.Count) % buttons.Count;
                buttons[next].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Enter when at >= 0:
                buttons[at].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                break;
        }
    }
}

/// <summary>Busca de filhos na árvore visual (só para o menu da face).</summary>
internal static class MenuVisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Visual root) where T : Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
