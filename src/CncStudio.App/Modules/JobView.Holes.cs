using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.App.Modules;

/// <summary>
/// CAM-FURO-01: furação reconhecida. Os furos verticais da peça STEP (<see cref="HoleRecognizer"/>) aparecem agrupados num painel;
/// cada grupo marcado vira uma Furação pela mesma rota do clique no 3D (<see cref="CamJobFromGeometry.Add"/>), sem ferramenta.
/// Só tela e criação de operação: nada novo no arquivo nem no G-code.
/// </summary>
public partial class JobView
{
    /// <summary>Id do item "Furação reconhecida…" no menu Operações ▾ (CamPalette).</summary>
    internal const string HolesActionId = "furacao-reconhecida";

    private const uint HoleBlue = 0x4C9BE8, HoleGray = 0x6B737D;

    private HoleReport? _holeReport;
    private bool _holePanelOpen;
    private readonly HashSet<int> _holeChecked = [];

    /// <summary>Lê os furos da peça (rápido, depois da análise das faces). Sem peça: some.</summary>
    private void ReadHoles(MeshFeatures features, TriangleMesh placed)
    {
        _holeReport = HoleRecognizer.Recognize(features, placed);
        _holeChecked.Clear();
        UpdatePalette();
    }

    private void ForgetHoles()
    {
        _holeReport = null;
        _holePanelOpen = false;
        _holeChecked.Clear();
    }

    /// <summary>Por que o item do menu está apagado (null = pronto).</summary>
    private string? HolesWhy() =>
        !StepReady || _holeReport is null
            ? "Abra a peça em STEP e mostre no 3D (cartão Peça pronta) para reconhecer os furos."
            : _holeReport.Groups.Count == 0 && _holeReport.Rejected.Count == 0
                ? "Nenhum furo achado na peça."
                : null;

    private void OpenHolePanel()
    {
        if (HolesWhy() is { } why)
        {
            Errors.Text = "• " + why;
            return;
        }

        _holePanelOpen = true;
        _holeChecked.Clear();
        for (int g = 0; g < _holeReport!.Groups.Count; g++)
        {
            if (!HoleRecognizer.IsCovered(_job, _holeReport.Groups[g]))
            {
                _holeChecked.Add(g);
            }
        }

        BuildProps();
        ShowPartInScene();
    }

    private void CloseHolePanel()
    {
        _holePanelOpen = false;
        BuildProps();
        ShowPartInScene();
    }

    /// <summary>Painel à direita: grupos com caixa de marcar, o que ficou de fora e os botões.</summary>
    private void HolePanel()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Furação reconhecida", FontSize = 18, FontWeight = FontWeight.Bold });
        PropsPanel.Children.Add(Note("Furos achados na peça STEP, agrupados por diâmetro e profundidade. Cada grupo marcado vira uma operação de Furação."));
        HoleReport report = _holeReport!;
        PropsPanel.Children.Add(Caps("GRUPOS"));
        for (int g = 0; g < report.Groups.Count; g++)
        {
            PropsPanel.Children.Add(HoleGroupCard(g, report.Groups[g]));
        }

        if (report.Groups.Count == 0)
        {
            PropsPanel.Children.Add(Note("Nenhum grupo pronto."));
        }

        if (report.Rejected.Count > 0)
        {
            PropsPanel.Children.Add(Caps("NÃO RECONHECIDOS"));
            foreach (IGrouping<string, HoleRejected> why in report.Rejected.GroupBy(r => r.Reason))
            {
                PropsPanel.Children.Add(UiKit.StatusNote($"{why.Count()} × Ø{why.First().Face.Diameter:0.##}: {why.Key}", "pending"));
            }
        }

        PropsPanel.Children.Add(Note("Só furos verticais (eixo Z). Ferramenta, ciclo e profundidade você confere na operação; nada é suposto."));
        int count = _holeChecked.Count(g => g < report.Groups.Count && !HoleRecognizer.IsCovered(_job, report.Groups[g]));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var close = new Button { Content = "Fechar", Padding = new Thickness(12, 5) };
        close.Click += (_, _) => CloseHolePanel();
        var create = new Button { Content = $"Criar operações ({count})", Padding = new Thickness(12, 5), IsEnabled = count > 0 };
        create.Click += (_, _) => CreateHoleOperations();
        buttons.Children.Add(close);
        buttons.Children.Add(create);
        PropsPanel.Children.Add(buttons);
    }

    private Control HoleGroupCard(int index, HoleGroup group)
    {
        bool covered = HoleRecognizer.IsCovered(_job, group);
        var box = new CheckBox { IsChecked = !covered && _holeChecked.Contains(index), IsEnabled = !covered, FontSize = 12 };
        string kind = group.Through ? "passante" : "cego";
        box.Content = $"{group.Faces.Count} × Ø{group.Diameter:0.##} · {kind}";
        box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked == true)
            {
                _holeChecked.Add(index);
            }
            else
            {
                _holeChecked.Remove(index);
            }

            ReloadHolePanel();
        };
        var card = new StackPanel { Spacing = 2 };
        card.Children.Add(box);
        card.Children.Add(Note($"Topo Z{group.TopZ:0.###} → fundo Z{group.BottomZ:0.###} · profundidade {group.Depth:0.###} mm"));
        if (covered)
        {
            card.Children.Add(UiKit.StatusNote("Já tem operação de Furação no trabalho.", "ok"));
        }
        else if (group.Deep)
        {
            card.Children.Add(Note("Furo profundo (mais de 3 × Ø): considere ciclo com bicada na operação."));
        }

        return new Border
        {
            Child = card, Padding = new Thickness(8, 6), Margin = new Thickness(0, 2), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = box.IsChecked == true ? CamColors.AccentBrush : CamColors.BorderBrush, Background = CamColors.CardBrush,
        };
    }

    private void ReloadHolePanel()
    {
        PropsPanel.Children.Clear();
        HolePanel();
        ShowPartInScene();
    }

    private void CreateHoleOperations()
    {
        if (_holeReport is not { } report || _partFeatures is not { } features)
        {
            return;
        }

        Snap();
        var made = new List<int>();
        string? error = null;
        for (int g = 0; g < report.Groups.Count && error is null; g++)
        {
            if (!_holeChecked.Contains(g) || HoleRecognizer.IsCovered(_job, report.Groups[g]))
            {
                continue;
            }

            (IReadOnlyList<int> one, error) = CamJobFromGeometry.Add(_job, JobOpKind.Drilling, features, report.Groups[g].Faces, [], PartTopZ);
            made.AddRange(one);
        }

        _holePanelOpen = false;
        _holeChecked.Clear();
        if (made.Count > 0)
        {
            Refresh(op: made[0]);
        }
        else
        {
            BuildProps();
        }

        ShowPartInScene();
        Errors.Text = error is not null
            ? "• " + error
            : $"• {made.Count} operação(ões) de Furação criada(s). Escolha a broca no banco ou digite T, Ø, rotação e avanços e confira o Z do fundo. " + CamJobPart.MeshContourNote;
    }

    /// <summary>Círculos no topo dos furos para o 3D: azul no grupo marcado, cinza no que ficou de fora (só com o painel aberto).</summary>
    private IReadOnlyList<(IReadOnlyList<Point3> Points, uint Color)> HoleLoops()
    {
        if (!_holePanelOpen || _holeReport is not { } report)
        {
            return [];
        }

        var list = new List<(IReadOnlyList<Point3>, uint)>();
        for (int g = 0; g < report.Groups.Count; g++)
        {
            uint color = _holeChecked.Contains(g) ? HoleBlue : HoleGray;
            list.AddRange(report.Groups[g].Faces.Select(f => (Circle(f), color)));
        }

        list.AddRange(report.Rejected.Select(r => (Circle(r.Face), HoleGray)));
        return list;
    }

    private static IReadOnlyList<Point3> Circle(MeshFace f)
    {
        double r = f.Diameter / 2;
        var pts = new List<Point3>();
        for (int i = 0; i < 48; i++)
        {
            double a = 2 * Math.PI * i / 48;
            pts.Add(new Point3(f.Center.X + (r * Math.Cos(a)), f.Center.Y + (r * Math.Sin(a)), f.Box.Max.Z));
        }

        return pts;
    }
}
