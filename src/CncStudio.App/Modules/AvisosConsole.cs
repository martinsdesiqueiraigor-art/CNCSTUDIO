using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Nível de uma mensagem do console, do mais grave ao menos grave.</summary>
public enum AvisoNivel { Erro, Aviso, Conferir, Info }

/// <summary>
/// Console de avisos (UI-PRO-P2), embaixo da vista do Trabalho fresa e torno. Junta num lugar só o que antes eram linhas laranja
/// sob o 3D (avisos do programa e T em conflito com o banco) e o texto vermelho da coluna da árvore (mensagens de andamento e
/// de erro). Cada linha tem nível: Erro, Aviso, Conferir ou Info. Recolhe e abre; recolhido, mostra a contagem. Só tela: os
/// textos vêm do Core e das telas, sem mudar.
/// </summary>
public sealed class AvisosConsole : Border
{
    private readonly TextBlock _title = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _arrow = new() { FontSize = 11, Foreground = CamColors.MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _header = new() { Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer _scroll = new() { MaxHeight = 92, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly StackPanel _rows = new() { Spacing = 2 };

    private IReadOnlyList<string> _warnings = [];
    private IReadOnlyList<string> _derived = [];
    private IReadOnlyList<string> _checks = [];
    private bool _open = true;

    /// <summary>Mensagens de andamento e de erro das telas (o que antes era o texto vermelho da árvore).</summary>
    public MessageChannel Messages { get; }

    public AvisosConsole()
    {
        Messages = new MessageChannel(this);
        Background = CamColors.PanelBrush;
        BorderBrush = CamColors.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(10, 5);
        _header.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _arrow, _title } };
        _header.Click += (_, _) => { _open = !_open; Render(); };
        ToolTip.SetTip(_header, "Recolher ou abrir o console de avisos");
        _scroll.Content = _rows;
        Child = new StackPanel { Children = { _header, _scroll } };
        Render();
    }

    /// <summary>Avisos do programa (do Core) e T em conflito com o banco ativo (nível Conferir).</summary>
    public void SetWarnings(IEnumerable<string> warnings, IEnumerable<string> checks)
    {
        _warnings = [.. warnings.Distinct()];
        _checks = [.. checks.Distinct()];
        Render();
    }

    /// <summary>Erros calculados pela tela (CAM-STOCK-03b: gouge da peça × bruto); nível Erro, trocados a cada cálculo.</summary>
    public void SetDerivedErrors(IEnumerable<string> errors)
    {
        List<string> next = [.. errors.Distinct()];
        if (next.SequenceEqual(_derived))
        {
            return;
        }

        _derived = next;
        Render();
    }

    /// <summary>Quantas mensagens há agora, por nível.</summary>
    public int Count(AvisoNivel level) => Entries().Count(e => e.Level == level);

    private IEnumerable<(AvisoNivel Level, string Text)> Entries()
    {
        var all = new List<(AvisoNivel, string)>();
        all.AddRange(Messages.Entries);
        all.AddRange(_derived.Select(w => (AvisoNivel.Erro, w)));
        all.AddRange(_warnings.Select(w => (AvisoNivel.Aviso, w)));
        all.AddRange(_checks.Select(w => (AvisoNivel.Conferir, w)));
        return all.OrderBy(e => (int)e.Item1);
    }

    private void Render()
    {
        var entries = Entries().ToList();
        _rows.Children.Clear();
        foreach ((AvisoNivel level, string text) in entries)
            _rows.Children.Add(Row(level, text));
        _arrow.Text = _open ? "▾" : "▸";
        _title.Text = Summary(entries);
        _scroll.IsVisible = _open && entries.Count > 0;
        IsVisible = true;
    }

    private static string Summary(List<(AvisoNivel Level, string Text)> entries)
    {
        if (entries.Count == 0)
            return "Avisos · nenhum";
        var parts = new List<string>();
        foreach (AvisoNivel level in Enum.GetValues<AvisoNivel>())
        {
            int n = entries.Count(e => e.Level == level);
            if (n > 0)
                parts.Add($"{n} {LevelName(level, n)}");
        }
        return $"Avisos ({entries.Count}) · " + string.Join(" · ", parts);
    }

    private static string LevelName(AvisoNivel level, int n) => level switch
    {
        AvisoNivel.Erro => n == 1 ? "erro" : "erros",
        AvisoNivel.Aviso => n == 1 ? "aviso" : "avisos",
        AvisoNivel.Conferir => "conferir",
        _ => "info",
    };

    private static Control Row(AvisoNivel level, string text)
    {
        (IBrush fg, IBrush bg) = level switch
        {
            AvisoNivel.Erro => UiKit.Status("bad"),
            AvisoNivel.Aviso => UiKit.Status("pending"),
            AvisoNivel.Conferir => (CamColors.SelectBrush, CamColors.SelectBgBrush),
            _ => UiKit.Status("neutral"),
        };
        string label = level switch { AvisoNivel.Erro => "Erro", AvisoNivel.Aviso => "Aviso", AvisoNivel.Conferir => "Conferir", _ => "Info" };
        var tag = new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1),
            Margin = new Thickness(0, 1, 8, 0),
            MinWidth = 56,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center },
        };
        var body = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = CamColors.TextBrush };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(tag);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>
    /// Mantém a escrita de antes (<c>Errors.Text = "• ..."</c>) das telas: cada linha vira uma mensagem do console, com o nível
    /// decidido pelo texto (Info para andamento, Conferir para coisa a olhar, Erro para falha). Texto vazio limpa.
    /// </summary>
    public sealed class MessageChannel
    {
        private readonly AvisosConsole _owner;
        private string _text = "";
        internal MessageChannel(AvisosConsole owner) => _owner = owner;

        internal List<(AvisoNivel, string)> Entries { get; private set; } = [];

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                Entries = [.. _text.Split('\n')
                    .Select(l => l.Trim().TrimStart('•', '⚠').Trim())
                    .Where(l => l.Length > 0)
                    .Select(l => (Classify(l), l))];
                _owner.Render();
            }
        }

        /// <summary>Nível pelo texto da mensagem. Fora das listas abaixo, a mensagem é tratada como erro (o mais seguro).</summary>
        internal static AvisoNivel Classify(string line)
        {
            string[] check = ["descarta", "ficaram Incompletas", "não foram achadas", "não desenha bruto", "O desenho já tem", "não é a mesma peça", "Preencha antes"];
            string[] info = ["Lendo ", "lido em", "Operação criada", "Trabalho novo", "Trabalho de torno novo", "Programa enviado", "trocada", "escolhida(s)", "Clique nas faces", "Abra o DXF do perfil"];
            if (check.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return AvisoNivel.Conferir;
            if (info.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return AvisoNivel.Info;
            return AvisoNivel.Erro;
        }
    }
}
