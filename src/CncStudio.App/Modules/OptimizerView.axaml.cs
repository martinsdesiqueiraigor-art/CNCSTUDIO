using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CncStudio.Core.Optimizer;

namespace CncStudio.App.Modules;

/// <summary>
/// Casillas Otimizador no desktop: analisa o programa da aba aberta e propõe laços Macro B SÓ PARA SIMULADOR.
/// Todo o cálculo é do Core (<see cref="OptimizerJson"/>); esta tela só mostra o resultado.
/// </summary>
public partial class OptimizerView : UserControl
{
    private JsonObject? _result;
    private string _programName = "programa.nc";

    /// <summary>Quem hospeda informa o programa da aba atual (texto e nome).</summary>
    public Func<(string Text, string Name)>? GetProgram { get; set; }

    /// <summary>Abre um texto numa aba nova do editor.</summary>
    public Action<string, string>? OpenInEditor { get; set; }

    /// <summary>Copia texto para a área de transferência.</summary>
    public Func<string, Task>? CopyText { get; set; }

    public OptimizerView()
    {
        InitializeComponent();
        AnalyzeButton.Click += (_, _) => Analyze();
        AnonymizeButton.Click += async (_, _) => await AnonymizeAsync();
        OpenProposalButton.Click += (_, _) =>
        {
            if (_result?["simulationProgram"]?.GetValue<string>() is { } program)
            {
                OpenInEditor?.Invoke(program, Path.GetFileNameWithoutExtension(_programName) + "_OTIMIZADO.nc");
            }
        };
        OpenReportButton.Click += (_, _) =>
        {
            if (_result?["document"]?.GetValue<string>() is { } doc)
            {
                OpenInEditor?.Invoke(doc, Path.GetFileNameWithoutExtension(_programName) + "_REVISAO.txt");
            }
        };
    }

    /// <summary>Chamado ao mostrar o módulo: atualiza o nome do programa da aba.</summary>
    public void Refresh()
    {
        if (GetProgram is null)
        {
            return;
        }

        (string text, string name) = GetProgram();
        _programName = name;
        int lines = string.IsNullOrEmpty(text) ? 0 : text.Count(c => c == '\n') + 1;
        ProgramLabel.Text = $"{name} · {lines} linhas";
    }

    private string ProfileName => ProfileCombo.SelectedIndex == 1 ? "convencional" : "calculadora";

    private void Analyze()
    {
        if (GetProgram is null)
        {
            return;
        }

        (string text, string name) = GetProgram();
        _programName = name;
        string json = OptimizerJson.Dispatch("analyze", new JsonObject { ["text"] = text, ["profile"] = ProfileName }.ToJsonString());
        _result = JsonNode.Parse(json) as JsonObject;
        if (_result?["ok"]?.GetValue<bool>() != true)
        {
            ResultPanel.IsVisible = false;
            Message.Text = _result?["error"]?.GetValue<string>() ?? "Não foi possível analisar.";
            Message.Foreground = Brushes.IndianRed;
            return;
        }

        Message.Text = "";
        try
        {
            Show(_result);
        }
        catch (Exception e)
        {
            ResultPanel.IsVisible = false;
            Message.Text = "Não foi possível mostrar o resultado: " + e.Message;
            Message.Foreground = Brushes.IndianRed;
        }
    }

    private void Show(JsonObject r)
    {
        string cam = r["cam"]?.GetValue<string>() ?? "";
        int operations = r["operations"]?.AsArray().Count ?? 0;
        int original = r["originalLines"]?.GetValue<int>() ?? 0;
        int converted = r["convertedLines"]?.GetValue<int>() ?? 0;
        int accepted = r["accepted"]?.GetValue<int>() ?? 0;
        int blocked = r["blocked"]?.GetValue<int>() ?? 0;
        SummaryTitle.Text = $"{operations} operação(ões) · CAM: {(cam.Length > 0 ? cam : "não reconhecido")}";
        SummaryText.Text =
            $"Linhas do original: {original}. Linhas da proposta: {converted}. Propostas aceitas: {accepted}. Bloqueadas pela conferência: {blocked}.\n" +
            "Não é promessa de redução de tempo: confirme tudo no simulador.";

        bool hasProgram = r["simulationProgram"] is not null;
        OpenProposalButton.IsEnabled = hasProgram;
        OpenReportButton.IsEnabled = r["document"] is not null;

        Proposals.Children.Clear();
        foreach (JsonNode? node in r["proposals"]?.AsArray() ?? [])
        {
            if (node is JsonObject p)
            {
                Proposals.Children.Add(Card(p));
            }
        }

        if (Proposals.Children.Count == 0)
        {
            Proposals.Children.Add(new TextBlock { Text = "Nenhuma repetição encontrada.", Opacity = 0.7 });
        }

        ResultPanel.IsVisible = true;
    }

    private static Control Card(JsonObject p)
    {
        bool ok = p["accepted"]?.GetValue<bool>() == true;
        var stack = new StackPanel { Spacing = 3 };
        string kind = p["kind"]?.GetValue<string>() ?? "";
        int start = p["startLine"]?.GetValue<int>() ?? 0;
        int end = p["endLine"]?.GetValue<int>() ?? 0;
        int saved = p["savedLines"]?.GetValue<int>() ?? 0;
        stack.Children.Add(new TextBlock
        {
            Text = $"#{p["number"]?.GetValue<int>()} · {kind} · linhas {start}–{end}",
            FontWeight = FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = ok ? $"Aceita pela conferência · economiza {saved} linhas" : "Bloqueada pela conferência",
            Foreground = ok ? Brushes.MediumSeaGreen : Brushes.Goldenrod,
            FontSize = 12,
        });
        foreach (string key in new[] { "blockers", "warnings" })
        {
            foreach (JsonNode? item in p[key]?.AsArray() ?? [])
            {
                stack.Children.Add(new TextBlock { Text = "• " + item?.GetValue<string>(), FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
            }
        }

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ok ? Brushes.SeaGreen : Brushes.DimGray,
            Child = stack,
        };
    }

    private async Task AnonymizeAsync()
    {
        if (GetProgram is null || CopyText is null)
        {
            return;
        }

        (string text, _) = GetProgram();
        JsonObject? r = JsonNode.Parse(OptimizerJson.Dispatch("anonymize", new JsonObject { ["text"] = text }.ToJsonString())) as JsonObject;
        if (r?["ok"]?.GetValue<bool>() == true)
        {
            await CopyText(r["text"]!.GetValue<string>());
            Message.Text = "Versão anonimizada copiada.";
            Message.Foreground = Brushes.MediumSeaGreen;
        }
        else
        {
            Message.Text = r?["error"]?.GetValue<string>() ?? "Não foi possível anonimizar.";
            Message.Foreground = Brushes.IndianRed;
        }
    }
}
