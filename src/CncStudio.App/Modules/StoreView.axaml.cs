using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Casillas Loja (teste) no desktop: catálogo simples e pedido pelo WhatsApp. Sem pagamento, sem preço no código.</summary>
public partial class StoreView : UserControl
{
    private const string Whats = "5519996816755";
    private readonly HashSet<string> _picked = [];
    private readonly List<(string Id, string Name)> _items = [];

    public StoreView()
    {
        InitializeComponent();
        OrderButton.Click += (_, _) => Order();
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("loja.catalogo.json");
            if (stream is null || JsonNode.Parse(stream) is not JsonObject data)
            {
                Notice.Text = "Não foi possível carregar o catálogo.";
                return;
            }

            Notice.Text = data["aviso"]?.GetValue<string>() ?? "";
            foreach (JsonNode? node in data["itens"]?.AsArray() ?? [])
            {
                if (node is JsonObject item)
                {
                    Items.Children.Add(Card(item));
                }
            }
        }
        catch (Exception e)
        {
            Notice.Text = "Não foi possível carregar o catálogo: " + e.Message;
        }
    }

    private Control Card(JsonObject item)
    {
        string id = item["id"]?.GetValue<string>() ?? "";
        string name = item["nome"]?.GetValue<string>() ?? "";
        _items.Add((id, name));
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = (item["categoria"]?.GetValue<string>() ?? "").ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.MediumTurquoise });
        stack.Children.Add(new TextBlock { Text = name, FontSize = 19, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = item["resumo"]?.GetValue<string>(), Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = item["descricao"]?.GetValue<string>(), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Avalonia.Thickness(0, 6) };
        int row = 0;
        foreach (JsonNode? spec in item["specs"]?.AsArray() ?? [])
        {
            string key = spec?[0]?.GetValue<string>() ?? "";
            string value = spec?[1]?.GetValue<string>() ?? "";
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = key, Opacity = 0.7, FontSize = 13, Margin = new Avalonia.Thickness(0, 2) };
            var v = new TextBlock { Text = value, FontSize = 13, Margin = new Avalonia.Thickness(0, 2), Foreground = value.StartsWith("A definir") ? Brushes.Goldenrod : null };
            Grid.SetRow(k, row);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(k);
            grid.Children.Add(v);
            row++;
        }

        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock { Text = "Preço: " + item["preco"]?.GetValue<string>(), FontWeight = FontWeight.SemiBold });
        var pick = new ToggleButton { Content = "Adicionar ao pedido", Padding = new Avalonia.Thickness(14, 8), HorizontalAlignment = HorizontalAlignment.Left };
        pick.IsCheckedChanged += (_, _) =>
        {
            if (pick.IsChecked == true)
            {
                _picked.Add(id);
            }
            else
            {
                _picked.Remove(id);
            }

            pick.Content = pick.IsChecked == true ? "✓ No pedido" : "Adicionar ao pedido";
            OrderButton.IsEnabled = _picked.Count > 0;
            OrderButton.Content = $"Pedir pelo WhatsApp ({_picked.Count})";
        };
        stack.Children.Add(pick);
        return new Border { CornerRadius = new Avalonia.CornerRadius(10), Padding = new Avalonia.Thickness(14), BorderThickness = new Avalonia.Thickness(1), BorderBrush = Brushes.DimGray, Child = stack };
    }

    private void Order()
    {
        string lines = string.Join("\n", _items.Where(i => _picked.Contains(i.Id)).Select(i => "• " + i.Name));
        string text = "Olá! Tenho interesse nestes itens da Casillas Loja:\n" + lines + "\n\nPode me passar preço e prazo?";
        string url = "https://wa.me/" + Whats + "?text=" + Uri.EscapeDataString(text);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Notice.Text = "Não foi possível abrir o navegador. Chame no WhatsApp (19) 99681-6755.";
        }
    }
}
