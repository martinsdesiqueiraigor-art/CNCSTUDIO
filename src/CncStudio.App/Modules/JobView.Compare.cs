using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Modules;

/// <summary>
/// CAM-STOCK-03b: peça × bruto na tela. O Core (<see cref="CamStockCompare"/>) compara o bruto simulado com a peça STEP fora da tela;
/// aqui ficam as cores no 3D (gouge vermelho, excesso âmbar), o selo de cada operação, a aba Verificação, o erro no console e a chave.
/// Só mostra: nada vai para o arquivo nem para o G-code. Verificação indicativa, nunca garantia.
/// </summary>
public partial class JobView
{
    /// <summary>Cor do gouge (cortou além da peça) e do excesso (ainda sobra material) no 3D e nas dicas.</summary>
    internal const uint GougeColor = 0xE05A5A, ExcessColor = 0xE0B040;

    private enum CompareView
    {
        /// <summary>Bruto depois da operação escolhida na árvore.</summary>
        Operation,

        /// <summary>Bruto depois da última operação (fim do programa).</summary>
        Final,
    }

    private bool _compareOn = true;
    private CompareView _compareView = CompareView.Operation;
    private CamStockCompare? _compare;
    private IReadOnlyList<CamStockCompareSummary>? _compareSummary;
    private string? _compareProblem;
    private CamStockCompareResult? _compareShown;
    private CompareRegion? _compareFocus;
    private StackPanel? _compareTab;

    /// <summary>Chave "Peça × bruto" vinda das preferências do app (nasce ligada).</summary>
    public bool StockCompare
    {
        get => _compareOn;
        set
        {
            _compareOn = value;
            OnCompareChanged();
        }
    }

    /// <summary>Guarda a chave nas preferências do app (nunca no trabalho).</summary>
    public Action<bool>? SaveStockCompare { get; set; }

    private void InitCompare()
    {
        StockCompareToggle.IsCheckedChanged += (_, _) => SetCompare(StockCompareToggle.IsChecked == true, save: true);
        Scene3D.CompareToggled += (_, on) => SetCompare(on, save: true);
        SyncCompareToggle();
    }

    private void SetCompare(bool on, bool save)
    {
        if (on == _compareOn)
        {
            return;
        }

        _compareOn = on;
        if (save)
        {
            SaveStockCompare?.Invoke(on);
        }

        OnCompareChanged();
    }

    private void OnCompareChanged()
    {
        if (!_compareOn)
        {
            _compareFocus = null;
        }

        SyncCompareToggle();
        ShowStockStates();
        UpdateStockScene();
        ShowPartInScene();
    }

    /// <summary>Por que a comparação não está disponível agora (null = disponível).</summary>
    private string? CompareUnavailable()
    {
        if (!_stockOn)
        {
            return "Ligue o bruto por operação para comparar com a peça.";
        }

        if (_job.PartStep is null)
        {
            return CamStockCompare.NoPart;
        }

        return _stockBusy ? null : _compareProblem;
    }

    private void SyncCompareToggle()
    {
        string? why = CompareUnavailable();
        StockCompareToggle.IsChecked = _compareOn;
        StockCompareToggle.Content = "Peça × bruto: " + (_compareOn ? "Ligada" : "Desligada");
        ToolTip.SetTip(StockCompareToggle, why is null
            ? "Compara o bruto de cada operação com a peça STEP: gouge (cortou além) e excesso (ainda sobra). Indicativo, nunca garantia."
            : "Sem comparação agora: " + why);
        Scene3D.SyncCompareChip(_compareOn, why is null, why);
    }

    /// <summary>Selo da comparação para a operação (texto, tipo de cor, dica) ou null para ficar com o selo do bruto.</summary>
    private (string Text, string Kind, string Tip)? CompareBadge(int index)
    {
        if (!_compareOn || _compare is null || _compareSummary is not { } list || index >= list.Count)
        {
            return null;
        }

        CamStockCompareSummary s = list[index];
        return s.Verdict switch
        {
            CamStockVerdict.Gouge => ("gouge", "bad", $"Cortou além da peça: {Cm3(s.GougeVolume)} (indicativo, nunca garantia)."),
            CamStockVerdict.Excess => ("excesso", "pending", $"Ainda sobra {Cm3(s.ExcessVolume)} acima da peça. É esperado no meio do programa."),
            CamStockVerdict.Ok => ("ok", "ok", "Dentro da tolerância da peça (indicativo)."),
            _ => null,
        };
    }

    /// <summary>Uma linha de erro por operação com gouge, para o console (nível Erro).</summary>
    private IEnumerable<string> CompareErrors()
    {
        if (!_compareOn || _compareSummary is not { } list)
        {
            yield break;
        }

        foreach (CamStockCompareSummary s in list.Where(x => x.Verdict == CamStockVerdict.Gouge))
        {
            string name = s.Index < _job.Operations.Count ? CamJobInfo.OpTitle(s.Index, _job.Operations[s.Index]) : $"Operação {s.Index + 1}";
            yield return $"Gouge na operação {name}: cortou {Cm3(s.GougeVolume)} além da peça (verificação indicativa, confira na simulação).";
        }
    }

    /// <summary>Retângulo da região escolhida em "Ver no 3D", no referencial do desenho, para somar aos laços da cena.</summary>
    private IReadOnlyList<(IReadOnlyList<Point3> Points, uint Color)> CompareFocusLoops()
    {
        if (!_compareOn || _compareFocus is not { } r || _stockTimeline?.Map is not { } map)
        {
            return [];
        }

        Point3 zero = CamJobZero.Position(_job) ?? default;
        double z = map.TopZ + zero.Z;
        Point3[] box =
        [
            new(r.MinX + zero.X, r.MinY + zero.Y, z), new(r.MaxX + zero.X, r.MinY + zero.Y, z),
            new(r.MaxX + zero.X, r.MaxY + zero.Y, z), new(r.MinX + zero.X, r.MaxY + zero.Y, z),
        ];
        return [(box, r.Kind == CompareCell.Gouge ? GougeColor : ExcessColor)];
    }

    /// <summary>
    /// Material, cores e resultado da comparação para o 3D, calculados fora da tela. O material é montado primeiro (com o instantâneo
    /// de <paramref name="index"/>); a comparação usa o mesmo instantâneo. Um cálculo de cada vez por bruto.
    /// </summary>
    private static (IReadOnlyList<SceneObjectState>? Material, IReadOnlyList<SceneObjectState>? Overlay, CamStockCompareResult? Result) MaterialAndCompare(
        CamStockTimeline timeline, CamStockCompare? compare, int index)
    {
        lock (timeline)
        {
            IReadOnlyList<SceneObjectState>? material = MaterialAfter(timeline, index);
            if (material is null || compare is null)
            {
                return (material, null, null);
            }

            CamStockCompareResult? result = compare.After(index);
            return (material, result is null ? null : Overlay(result, timeline.Map!), result);
        }
    }

    /// <summary>Malhas planas de gouge e de excesso um fio acima do material (corridas de células iguais, para ficar leve).</summary>
    private static IReadOnlyList<SceneObjectState> Overlay(CamStockCompareResult result, HeightMap map)
    {
        var list = new List<SceneObjectState>();
        foreach ((CompareCell kind, string id, uint color) in new[] { (CompareCell.Gouge, "gouge", GougeColor), (CompareCell.Excess, "excesso", ExcessColor) })
        {
            var triangles = new List<(Point3, Point3, Point3)>();
            ReadOnlySpan<float> heights = map.Heights;
            double cell = result.CellSize;
            for (int row = 0; row < result.Rows; row++)
            {
                int column = 0;
                while (column < result.Columns)
                {
                    if (result.CellAt(column, row) != kind)
                    {
                        column++;
                        continue;
                    }

                    int start = column;
                    float h = heights[(row * result.Columns) + column];
                    while (column < result.Columns && result.CellAt(column, row) == kind && Math.Abs(heights[(row * result.Columns) + column] - h) < 1e-3)
                    {
                        column++;
                    }

                    double x0 = result.OriginX + (start * cell), x1 = result.OriginX + (column * cell);
                    double y0 = result.OriginY + (row * cell), y1 = y0 + cell;
                    double z = h + 0.03; // um fio acima da superfície, para não sumir dentro dela
                    triangles.Add((new Point3(x0, y0, z), new Point3(x1, y0, z), new Point3(x1, y1, z)));
                    triangles.Add((new Point3(x0, y0, z), new Point3(x1, y1, z), new Point3(x0, y1, z)));
                }
            }

            if (triangles.Count > 0)
            {
                list.Add(new SceneObjectState(id, SceneRole.Fixture, new TriangleMesh(triangles).ToSolidMesh(color), 1, Point3.Origin, true));
            }
        }

        return list;
    }

    // ---------------------------------------------------------------- aba Verificação

    /// <summary>Conteúdo da aba Verificação da operação (painel da aba que o <see cref="OpTabs"/> criou).</summary>
    private void AddCompareTab(int index, StackPanel panel)
    {
        _compareTab = panel;
        panel.Tag = index;
        FillCompareTab();
    }

    private void FillCompareTab()
    {
        if (_compareTab is not { Tag: int index } panel)
        {
            return;
        }

        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = "PEÇA × BRUTO DEPOIS DESTA OPERAÇÃO", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = CamColors.MutedBrush });
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        modes.Children.Add(ModeButton("Desligada", !_compareOn, () => SetCompare(false, save: true)));
        modes.Children.Add(ModeButton("Esta operação", _compareOn && _compareView == CompareView.Operation, () => PickCompareView(CompareView.Operation)));
        modes.Children.Add(ModeButton("Fim do programa", _compareOn && _compareView == CompareView.Final, () => PickCompareView(CompareView.Final)));
        panel.Children.Add(modes);

        if (CompareUnavailable() is { } why)
        {
            panel.Children.Add(UiKit.StatusNote(why, "neutral"));
            panel.Children.Add(UiKit.StatusNote(CamStockCompare.Disclaimer, "neutral"));
            return;
        }

        if (!_compareOn)
        {
            panel.Children.Add(new TextBlock { Text = "Comparação desligada.", FontSize = 12, Opacity = 0.75 });
            return;
        }

        if (_stockBusy || _compare is null)
        {
            panel.Children.Add(new TextBlock { Text = "Calculando…", FontSize = 12, Opacity = 0.75 });
            return;
        }

        CamStockCompareResult? r = _compareShown;
        if (r is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = _job.Operations.ElementAtOrDefault(index)?.Enabled == false
                    ? "Operação desligada: o bruto passa igual."
                    : "Esta operação não tem bruto calculado: veja o estado dela na árvore.",
                FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(UiKit.StatusNote(CamStockCompare.Disclaimer, "neutral"));
            return;
        }

        string pt(double v) => v.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        panel.Children.Add(Line("Tolerância", pt(r.Tolerance) + " mm"));
        panel.Children.Add(Line("Gouge", Cm3(r.GougeVolume)));
        panel.Children.Add(Line("Excesso", Cm3(r.ExcessVolume)));
        panel.Children.Add(Line("Dentro da tolerância", r.WithinPercent.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " %"));
        panel.Children.Add(new TextBlock { Text = "REGIÕES", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = CamColors.MutedBrush, Margin = new Thickness(0, 6, 0, 0) });
        if (r.Regions.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nenhuma: tudo dentro da tolerância.", FontSize = 12, Opacity = 0.75 });
        }

        foreach (CompareRegion region in r.Regions.Take(6))
        {
            bool gouge = region.Kind == CompareCell.Gouge;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = $"{(gouge ? "Gouge" : "Excesso")} · {Cm3(region.Volume)} · X {region.MinX:0}…{region.MaxX:0}, Y {region.MinY:0}…{region.MaxY:0}",
                FontSize = 12, Foreground = Brush.Parse(gouge ? "#F5A5A5" : "#F0D9A0"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            });
            bool focused = _compareFocus == region;
            var show = new Button { Content = focused ? "Esconder" : "Ver no 3D", Padding = new Thickness(8, 2), FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
            show.Click += (_, _) =>
            {
                _compareFocus = _compareFocus == region ? null : region;
                if (_compareFocus is not null && View3DButton.IsChecked != true)
                {
                    View3DButton.IsChecked = true;
                }

                ShowPartInScene();
                FillCompareTab();
            };
            Grid.SetColumn(show, 1);
            row.Children.Add(show);
            panel.Children.Add(row);
        }

        if (r.Regions.Count > 6)
        {
            panel.Children.Add(new TextBlock { Text = $"+ {r.Regions.Count - 6} regiões menores.", FontSize = 11, Opacity = 0.7 });
        }

        panel.Children.Add(UiKit.StatusNote(CamStockCompare.Disclaimer + " Só mostra: nada vai para o arquivo nem para o G-code.", "neutral"));
    }

    private static ToggleButton ModeButton(string text, bool on, Action pick)
    {
        var b = new ToggleButton { Content = text, IsChecked = on, Padding = new Thickness(8, 3), FontSize = 11, MinHeight = 24 };
        b.Click += (_, _) => pick();
        return b;
    }

    private void PickCompareView(CompareView view)
    {
        _compareFocus = null;
        _compareView = view;
        if (!_compareOn)
        {
            SetCompare(true, save: true);
            return;
        }

        UpdateStockScene();
        ShowPartInScene();
        FillCompareTab();
    }
}
