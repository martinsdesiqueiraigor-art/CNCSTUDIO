using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CncStudio.Web;

/// <summary>
/// Teclado próprio do editor para G-code (celular): letras na ordem do dia a dia do CNC
/// (G M N O X Y Z...), números e símbolos em outra página, setas, apagar e Enter.
/// Não toma o foco do editor (o cursor fica onde estava) e não abre o teclado do Android.
/// Cada tecla manda um texto ("G", "7"...) ou um comando: back, enter, space, left, right,
/// up, down, undo, hide. Apagar e setas repetem enquanto estão apertados.
/// </summary>
internal sealed class EditorKeyboard : Border
{
    private static readonly string[] LetterRows =
    [
        "G M N O X Y Z I J K",
        "F S T H D R P Q L A",
        "B C E U V W ( ) ; /",
    ];

    private static readonly string[] SymbolRows =
    [
        "1 2 3 4 5 6 7 8 9 0",
        ". - + = * # [ ] , %",
        ": ' \" < > _ $ @ & !",
    ];

    private readonly StackPanel _letters = new() { Spacing = 3 };
    private readonly StackPanel _symbols = new() { Spacing = 3, IsVisible = false };
    private readonly Button _pageButton;
    private readonly List<Button> _keys = [];
    private readonly DispatcherTimer _repeat = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private string? _repeating;
    private int _heldMs;

    /// <summary>Uma tecla foi apertada: o texto a inserir ou o nome do comando.</summary>
    public event EventHandler<string>? KeyPressed;

    public EditorKeyboard()
    {
        // Página das letras (G M N O X Y Z...) e página dos números e símbolos; a de baixo é comum às duas
        foreach (string row in LetterRows)
        {
            _letters.Children.Add(BuildRow(row));
        }

        foreach (string row in SymbolRows)
        {
            _symbols.Children.Add(BuildRow(row));
        }

        _pageButton = Make("123", "page", fontSize: 13);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("1.3*,*,*,*,*,2.2*,1.5*,1.4*,1.3*,*") };
        Place(bottom, _pageButton, 0);
        // Ícones desenhados (a fonte do app não tem todas as setas e símbolos)
        Place(bottom, Make("", "left", repeat: true, icon: "M15,5 L8,12 L15,19 Z"), 1);
        Place(bottom, Make("", "right", repeat: true, icon: "M9,5 L16,12 L9,19 Z"), 2);
        Place(bottom, Make("", "up", repeat: true, icon: "M5,15 L12,8 L19,15 Z"), 3);
        Place(bottom, Make("", "down", repeat: true, icon: "M5,9 L12,16 L19,9 Z"), 4);
        Place(bottom, Make("espaço", "space", fontSize: 12), 5);
        Place(bottom, Make("Apagar", "back", repeat: true, fontSize: 11), 6);
        Place(bottom, Make("Enter", "enter", fontSize: 11), 7);
        Place(bottom, Make("Desf.", "undo", fontSize: 11), 8);
        Place(bottom, Make("", "hide", icon: "M4,8 H20 L12,17 Z M4,19 H20 V21 H4 Z"), 9);

        var stack = new StackPanel { Spacing = 3, Margin = new Thickness(4, 4, 4, 4) };
        stack.Children.Add(_letters);
        stack.Children.Add(_symbols);
        stack.Children.Add(bottom);
        Child = stack;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Focusable = false;

        _repeat.Tick += (_, _) =>
        {
            // Segura um pouco antes de repetir (como o teclado do aparelho)
            if (_heldMs < 400)
            {
                _heldMs += (int)_repeat.Interval.TotalMilliseconds;
                return;
            }

            if (_repeating is { } command)
            {
                KeyPressed?.Invoke(this, command);
            }
        };
    }

    /// <summary>Altura das teclas: menor no celular deitado, para sobrar espaço para o código.</summary>
    public void SetCompact(bool compact)
    {
        foreach (Button key in _keys)
        {
            key.MinHeight = compact ? 30 : 40;
            key.Height = compact ? 30 : 40;
        }
    }

    private Control BuildRow(string row)
    {
        string[] items = row.Split(' ');
        var grid = new UniformGrid { Rows = 1, Columns = items.Length };
        foreach (string item in items)
        {
            grid.Children.Add(Make(item, item));
        }

        return grid;
    }

    private static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private Button Make(string label, string command, bool repeat = false, double fontSize = 16, string? icon = null)
    {
        var button = new Button
        {
            Content = icon is null ? label : new PathIcon { Data = Geometry.Parse(icon), Width = 16, Height = 16 },
            Focusable = false,
            Classes = { "bar" },
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 40,
            Height = 40,
            Margin = new Thickness(1.5, 0),
            FontSize = fontSize,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ClickMode = ClickMode.Press,
        };
        if (command == "hide")
        {
            ToolTip.SetTip(button, "Esconder o teclado");
        }

        // Age ao encostar (digitar rápido) e não toma o foco do editor
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            e.Handled = true;
            Send(command, label);
            if (repeat)
            {
                _repeating = command;
                _heldMs = 0;
                _repeat.Start();
            }
        }, RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, _) => StopRepeat(), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => StopRepeat();
        button.PointerExited += (_, _) => StopRepeat();
        _keys.Add(button);
        return button;
    }

    private void Send(string command, string label)
    {
        if (command == "page")
        {
            bool toSymbols = _letters.IsVisible;
            _letters.IsVisible = !toSymbols;
            _symbols.IsVisible = toSymbols;
            _pageButton.Content = toSymbols ? "ABC" : "123";
            return;
        }

        KeyPressed?.Invoke(this, command);
    }

    private void StopRepeat()
    {
        _repeat.Stop();
        _repeating = null;
    }
}
