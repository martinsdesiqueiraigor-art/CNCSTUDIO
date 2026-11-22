using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Assist;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.App.Modules;

/// <summary>
/// STEP-OPS S0: selecionar faces no 3D e escolher a operação na faixa (como nos outros CAMs), e editar a geometria da operação depois,
/// pela seção "Geometria" do painel (trocar, somar, tirar, filtro de laços). A operação guarda a impressão das faces (<see cref="GeoSource"/>),
/// então clicar nela na árvore acende as faces dela no 3D.
/// </summary>
public partial class JobView
{
    /// <summary>Peça STEP lida e analisada: a seleção no 3D vale para a faixa.</summary>
    private bool StepReady => _job.PartStep is not null && _partFeatures is not null && _partMesh is not null;

    private IReadOnlyList<MeshFace> SelectedFaces() =>
        _partFeatures is { } f ? [.. _partSelection.Where(i => i >= 0 && i < f.Faces.Count).Select(i => f.Faces[i])] : [];

    private MeshFeatures? _stepPlanFor;
    private StepPlanDrawing? _stepPlan;

    /// <summary>A peça STEP lida como desenho para o Assist (guardada enquanto a análise da malha for a mesma).</summary>
    public StepPlanDrawing? StepPlan
    {
        get
        {
            if (_partFeatures is not { } f || _partMesh is not { } m || _job.PartStep is null)
            {
                return null;
            }

            if (!ReferenceEquals(_stepPlanFor, f))
            {
                _stepPlan = StepPlanSource.FromMesh(f, m.Placed.Bounds);
                _stepPlanFor = f;
            }

            return _stepPlan;
        }
    }

    /// <summary>Plano do Assist sobre o STEP: junta os contornos lidos ao trabalho e as operações, com os índices dos contornos ajustados.</summary>
    public void AddStepPlan(IReadOnlyList<Contour> contours, IReadOnlyList<CamOperationSpec> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        Snap();
        int offset = _job.Contours.Count;
        _job.Contours.AddRange(contours);
        int first = _job.Operations.Count;
        foreach (CamOperationSpec o in operations)
        {
            _job.Operations.Add(o.ContourIndex >= 0 ? o with { ContourIndex = o.ContourIndex + offset } : o);
        }

        Refresh(op: first);
    }

    // UI-PRO-P3a-2: geometria da operação na árvore (só leitura). Passar o mouse numa linha acende no 3D só aquela parte.
    private GeoSource? _hoverGeometry;
    private readonly HashSet<int> _geoExpanded = [];

    /// <summary>Nó "Geometria" da operação feita no 3D, com uma linha por face ou aresta (só leitura; o mouse acende no 3D).</summary>
    private TreeViewItem? GeometryNode(int opIndex, CamOperationSpec spec)
    {
        if (spec.Geometry is not { } geo || _partFeatures is not { } features)
        {
            return null;
        }

        (_, _, IReadOnlyList<GeoRef> lost) = JobGeometry.Resolve(features, geo);
        TreeViewItem Line(string text, GeoSource? hover, bool bad = false)
        {
            Grid row = TreeRow(text, bad ? ("pending", "não achada no STEP") : null, null, null, hover is null ? "Todas as faces e arestas desta operação" : "Passe o mouse para acender no 3D");
            row.PointerEntered += (_, _) => HoverGeometry(hover ?? geo);
            row.PointerExited += (_, _) => HoverGeometry(null);
            return UiKit.StateItem(row, new TreeTag("geo", opIndex));
        }

        TreeViewItem node = Line($"Geometria ({geo.Refs.Count})", null);
        foreach (GeoRef r in geo.Refs)
        {
            node.Items.Add(Line(JobGeometry.Describe(r), geo with { Refs = [r] }, lost.Contains(r)));
        }

        // CAM-ILHAS-02: no Automático, uma linha por papel ("1 ilha", "8 furos ignorados", "borda aberta") na cor do 3D
        foreach (IGrouping<LoopRole, LoopItem> g in AutoLoops(opIndex).GroupBy(l => l.Role).OrderBy(g => g.Key switch { LoopRole.Island => 0, LoopRole.Ignored => 1, LoopRole.OpenBorder or LoopRole.ClosedBorder => 2, _ => 3 }))
        {
            int n = g.Count();
            string text = g.Key switch
            {
                LoopRole.Island => n == 1 ? "1 ilha" : $"{n} ilhas",
                LoopRole.Ignored => n == 1 ? "1 furo ignorado" : $"{n} furos ignorados",
                LoopRole.OpenBorder => "borda aberta",
                LoopRole.ClosedBorder => "bolsão fechado",
                _ => n == 1 ? "1 laço misto" : $"{n} laços mistos",
            };
            Grid row = TreeRow(text, null, LoopSwatch(g.Key), null, "Passe o mouse para acender no 3D");
            if (row.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            {
                label.Foreground = LoopBrush(g.Key);
            }

            LoopRole role = g.Key;
            row.PointerEntered += (_, _) => HoverLoop(role);
            row.PointerExited += (_, _) => HoverLoop(null);
            node.Items.Add(UiKit.StateItem(row, new TreeTag("geo", opIndex)));
        }

        node.IsExpanded = _geoExpanded.Contains(opIndex);
        node.Expanded += (_, _) => _geoExpanded.Add(opIndex);
        node.Collapsed += (_, _) => _geoExpanded.Remove(opIndex);
        return node;
    }

    private void HoverGeometry(GeoSource? geo)
    {
        _hoverGeometry = geo;
        ShowPartInScene();
    }

    /// <summary>Faces e arestas da linha de geometria sob o mouse na árvore.</summary>
    private (IEnumerable<int> Triangles, IEnumerable<IReadOnlyList<Point3>> Edges) HoverParts()
    {
        if (_hoverGeometry is not { } geo || _partFeatures is not { } features)
        {
            return ([], []);
        }

        (IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, _) = JobGeometry.Resolve(features, geo);
        return (faces.SelectMany(f => f.Triangles), edges.Select(e => e.Points));
    }

    /// <summary>Pontos das arestas a acender: as escolhidas no 3D e as da operação escolhida na árvore.</summary>
    private IReadOnlyList<IReadOnlyList<Point3>> HighlightedEdges()
    {
        var chains = new List<IReadOnlyList<Point3>>(SelectedEdges().Select(e => e.Points));
        if (_partFeatures is { } features && SelectedOp() is { } i && i < _job.Operations.Count && _job.Operations[i].Geometry is { } geo)
        {
            chains.AddRange(JobGeometry.Resolve(features, geo).Edges.Select(e => e.Points));
        }

        chains.AddRange(HoverParts().Edges);
        return chains;
    }

    private IReadOnlyList<MeshEdge> SelectedEdges() =>
        _partFeatures is { } f ? [.. _partEdges.Where(i => i >= 0 && i < f.Edges.Count).Select(i => f.Edges[i])] : [];

    private bool HasSelection => _partSelection.Count + _partEdges.Count > 0;

    private double PartTopZ => _partMesh?.Placed.Bounds.Max.Z ?? 0;

    /// <summary>Faixa com peça STEP lida: o botão serve à seleção do 3D (null = serve, texto = por que não). Sem peça STEP: null = regra de sempre.</summary>
    private string? StepPaletteWhy(CamPaletteItem item)
    {
        if (!StepReady || item.Kind is not { } kind)
        {
            return null;
        }

        if (kind is not (JobOpKind.Pocket or JobOpKind.Drilling or JobOpKind.Facing or JobOpKind.Contour))
        {
            return null;
        }

        return CamJobFromGeometry.Unavailable(kind, _partFeatures!, SelectedFaces(), SelectedEdges(), PartTopZ, item.Side);
    }

    /// <summary>Botão da faixa com seleção no 3D: cria a operação da seleção (uma por fundo de bolsão; uma furação com todos os furos).</summary>
    private bool AddFromSelection(CamPaletteItem item)
    {
        if (!StepReady || item.Kind is not { } kind || kind is not (JobOpKind.Pocket or JobOpKind.Drilling or JobOpKind.Facing or JobOpKind.Contour))
        {
            return false;
        }

        if (!HasSelection && _job.Contours.Count > 0 && kind is JobOpKind.Contour or JobOpKind.Facing)
        {
            return false; // contornos já no trabalho: a regra de sempre
        }

        Snap();
        int before = _job.Operations.Count;
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(_job, kind, _partFeatures!, SelectedFaces(), SelectedEdges(), PartTopZ, item.Side);
        if (error is not null)
        {
            Errors.Text = "• " + error;
            return true;
        }

        if (item.Id == CamPalette.AdaptiveId)
        {
            // CAM-ADAPT-01c: o Bolsão criado da seleção vira desbaste adaptativo
            foreach (int k in made)
            {
                _job.Operations[k] = CamPalette.AsAdaptive(_job.Operations[k]);
            }
        }

        MarkNewOps(before);
        _partSelection.Clear();
        _partEdges.Clear();
        Refresh(op: made[0]);
        ShowPartInScene();
        Errors.Text = "• Operação criada da seleção. Digite T, Ø, rotação, avanços e confira o Z do fundo. " + CamJobPart.MeshContourNote;
        return true;
    }

    /// <summary>Faces (triângulos) da operação escolhida na árvore, achadas na peça lida: acendem no 3D.</summary>
    private IEnumerable<int> SelectedOpTriangles()
    {
        if (_partFeatures is not { } features || SelectedOp() is not { } i || i >= _job.Operations.Count || _job.Operations[i].Geometry is not { } geo)
        {
            return [];
        }

        if (AutoLoops(i).Count > 0)
        {
            return []; // CAM-ILHAS-02: no Automático os laços coloridos mostram a face; o laranja por cima esconderia a borda aberta
        }

        (IReadOnlyList<MeshFace> faces, _, _) = JobGeometry.Resolve(features, geo);
        return faces.SelectMany(f => f.Triangles);
    }

    /// <summary>Seção "Geometria" do painel da operação feita no 3D: lista, trocar, somar, tirar, laços e o aviso de geometria não achada.</summary>
    private void GeometrySection(int i, CamOperationSpec s)
    {
        if (s.Geometry is not { } geo)
        {
            return;
        }

        PropsPanel.Children.Add(Caps("GEOMETRIA (DO 3D)"));
        if (_partFeatures is not { } features)
        {
            PropsPanel.Children.Add(Note("Mostre a peça no 3D (lê o STEP) para ver e editar a geometria desta operação."));
            return;
        }

        (IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, IReadOnlyList<GeoRef> lost) = JobGeometry.Resolve(features, geo);
        foreach (GeoRef r in geo.Refs)
        {
            bool isLost = lost.Contains(r);
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0), FontSize = 11, IsEnabled = geo.Refs.Count > 1 };
            ToolTip.SetTip(remove, geo.Refs.Count > 1 ? "Tirar esta face da operação." : "A operação precisa de ao menos uma face.");
            GeoRef captured = r;
            remove.Click += (_, _) => ChangeGeometry(i, [.. geo.Refs.Where(x => x != captured)], geo.Loops);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(new TextBlock { Text = JobGeometry.Describe(r) + (isLost ? " — não achada no STEP" : ""), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
            PropsPanel.Children.Add(row);
        }

        if (lost.Count > 0)
        {
            PropsPanel.Children.Add(UiKit.StatusNote("Reselecionar: o STEP mudou e nem todas as faces foram achadas. O contorno guardado continua como estava; selecione as faces no 3D e use Trocar seleção.", "pending"));
        }

        var buttons = new List<Control>();
        bool hasSelection = HasSelection;
        buttons.Add(Choice("Trocar seleção", false, hasSelection ? () => ChangeGeometry(i, SelectedRefs(), geo.Loops) : null,
            "Troca as faces da operação pelas que estão selecionadas no 3D (T, avanços e alturas digitados ficam)."));
        buttons.Add(Choice("+ Adicionar", false, hasSelection ? () => ChangeGeometry(i, [.. geo.Refs, .. SelectedRefs().Where(r => !geo.Refs.Contains(r))], geo.Loops) : null,
            "Soma ao que a operação já usa as faces selecionadas no 3D."));
        PropsPanel.Children.Add(Choices([.. buttons]));
        if (!hasSelection)
        {
            PropsPanel.Children.Add(Note("Para trocar ou somar, selecione faces no 3D (Ctrl soma)."));
        }

        if (s.Kind is JobOpKind.Pocket or JobOpKind.Contour && geo.Refs.Any(r => r.Kind == GeoKind.Face))
        {
            PropsPanel.Children.Add(Caps("LAÇOS"));
            if (s.Kind == JobOpKind.Pocket)
            {
                PropsPanel.Children.Add(Choices(
                    Choice("Automático", geo.Loops == LoopFilter.Auto, () => ChangeGeometry(i, geo.Refs, LoopFilter.Auto), "Como os outros CAMs: o que sobe vira ilha, furo fica de fora e a borda que desce vai até o bruto."),
                    Choice("Todos", geo.Loops == LoopFilter.All, () => ChangeGeometry(i, geo.Refs, LoopFilter.All), "Contorno de fora e as ilhas (e furos) dentro da face."),
                    Choice("Só o de fora", geo.Loops == LoopFilter.Outer, () => ChangeGeometry(i, geo.Refs, LoopFilter.Outer), "Esvazia tudo, sem deixar ilhas.")));
                if (geo.Loops == LoopFilter.Auto)
                {
                    LoopListSection(AutoLoops(i));
                }
            }
            else
            {
                PropsPanel.Children.Add(Choices(
                    Choice("Externo", geo.Loops != LoopFilter.Inner, () => ChangeGeometry(i, geo.Refs, LoopFilter.Outer), "O laço de fora da face."),
                    Choice("Interno", geo.Loops == LoopFilter.Inner, () => ChangeGeometry(i, geo.Refs, LoopFilter.Inner), "O laço de dentro da face (um furo ou ilha só).")));
            }
        }

        double? model = CamJobFromGeometry.ModelBottomZ(s.Kind, faces);
        if (model is { } z && Math.Abs(z - s.Levels.BottomZ) > 1e-6)
        {
            var take = Choice($"Pegar o Z do fundo do modelo ({Fmt(z)} mm)", false, () => SetBottomFromModel(i, z), "O Z do fundo digitado é diferente do que a face mostra. Só você troca.");
            PropsPanel.Children.Add(Choices(take));
        }

    }

    private IReadOnlyList<GeoRef> SelectedRefs() => [.. SelectedFaces().Select(JobGeometry.Of), .. SelectedEdges().Select(JobGeometry.Of)];

    private void SetBottomFromModel(int i, double z)
    {
        Snap();
        CamOperationSpec s = _job.Operations[i];
        _job.Operations[i] = CamJobInfo.SetMissing(s with { Levels = s.Levels with { BottomZ = z } }, CamJobPart.BottomField, false);
        Refresh(op: i);
    }

    /// <summary>Troca a geometria da operação (lê o contorno da malha de novo). T, avanços e alturas digitados ficam.</summary>
    private void ChangeGeometry(int i, IReadOnlyList<GeoRef> refs, LoopFilter loops)
    {
        if (_partFeatures is not { } features || i >= _job.Operations.Count)
        {
            return;
        }

        var faces = new List<MeshFace>();
        var edges = new List<MeshEdge>();
        foreach (GeoRef r in refs)
        {
            if (r.Kind == GeoKind.Face && JobGeometry.Find(features, r) is { } f)
            {
                faces.Add(f);
            }
            else if (r.Kind == GeoKind.Edge && JobGeometry.FindEdge(features, r) is { } e)
            {
                edges.Add(e);
            }
        }

        if (faces.Count + edges.Count != refs.Count)
        {
            Errors.Text = "• Há faces ou arestas que não foram achadas no STEP: selecione de novo no 3D e use Trocar seleção.";
            return;
        }

        Snap();
        if (CamJobFromGeometry.Update(_job, i, features, faces, edges, loops, PartTopZ) is { } error)
        {
            Errors.Text = "• " + error;
            return;
        }

        _partSelection.Clear();
        _partEdges.Clear();
        Refresh(op: i);
        ShowPartInScene();
        Errors.Text = "• Geometria da operação trocada. " + CamJobPart.MeshContourNote;
    }

    // CAM-ILHAS-02: o que o bolsão automático faz com cada laço (resumo, lista, linhas na árvore e cores no 3D)
    private (MeshFeatures? Features, GeoSource? Geo, IReadOnlyList<LoopItem> Items) _loopCache = (null, null, []);
    private LoopRole? _hoverLoop;

    /// <summary>Laços do bolsão automático da operação <paramref name="i"/> (vazio se não for bolsão automático ou sem peça lida).</summary>
    private IReadOnlyList<LoopItem> AutoLoops(int i)
    {
        if (_partFeatures is not { } features || i < 0 || i >= _job.Operations.Count
            || _job.Operations[i] is not { Kind: JobOpKind.Pocket, Geometry: { Loops: LoopFilter.Auto } geo })
        {
            return [];
        }

        if (!ReferenceEquals(_loopCache.Features, features) || _loopCache.Geo != geo)
        {
            (IReadOnlyList<MeshFace> faces, _, _) = JobGeometry.Resolve(features, geo);
            _loopCache = (features, geo, PocketLoopReport.Describe(features, faces));
        }

        return _loopCache.Items;
    }

    /// <summary>Laços da operação escolhida para o 3D, cada um na cor do papel; o papel sob o mouse na árvore acende em branco.</summary>
    private IReadOnlyList<(IReadOnlyList<Point3> Points, uint Color)> SceneLoops() =>
        SelectedOp() is { } i
            ? [.. AutoLoops(i).Select(l => (l.Points, _hoverLoop == l.Role ? 0xFFFFFFu : l.Role == LoopRole.Ignored ? 0x4A5058u : PocketLoopReport.ColorOf(l.Role)))]
            : [];

    private void HoverLoop(LoopRole? role)
    {
        _hoverLoop = role;
        ShowPartInScene();
    }

    private static IBrush LoopBrush(LoopRole role) => new SolidColorBrush(Color.FromUInt32(0xFF000000 | PocketLoopReport.ColorOf(role)));

    /// <summary>Traço curto na cor do papel (cheio na ilha; dividido no ignorado e na borda aberta, como na imagem aprovada).</summary>
    private static Control LoopSwatch(LoopRole role)
    {
        IBrush brush = LoopBrush(role);
        bool dashed = role is LoopRole.Ignored or LoopRole.OpenBorder;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Width = 14 };
        if (dashed)
        {
            panel.Children.Add(new Border { Width = 6, Height = 3, Background = brush });
            panel.Children.Add(new Border { Width = 6, Height = 3, Background = brush });
        }
        else
        {
            panel.Children.Add(new Border { Width = 14, Height = 3, Background = brush });
        }

        return panel;
    }

    /// <summary>Resumo e lista dos laços na aba Estratégia (só no filtro Automático).</summary>
    private void LoopListSection(IReadOnlyList<LoopItem> items)
    {
        if (items.Count == 0)
        {
            PropsPanel.Children.Add(Note("Mostre a peça no 3D (lê o STEP) para ver o que o Automático faz com cada laço."));
            return;
        }

        string summary = PocketLoopReport.Summary(items);
        string detail = items.Any(l => l.Role == LoopRole.OpenBorder)
            ? "A borda vai até o bruto + raio da fresa + 1 mm."
            : items.Any(l => l.Role == LoopRole.Mixed) ? "Limite misto: escolha Todos ou Só o de fora." : "A fresa fica dentro da parede de fora.";
        PropsPanel.Children.Add(UiKit.StatusNote(summary + ". " + detail, items.Any(l => l.Role == LoopRole.Mixed) ? "bad" : "info"));
        foreach (LoopGroup g in PocketLoopReport.Group(items))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Background = Brushes.Transparent };
            var effect = new TextBlock { Text = g.Effect, FontSize = 11, Foreground = LoopBrush(g.Role), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(effect, Dock.Right);
            row.Children.Add(effect);
            Control sw = LoopSwatch(g.Role);
            sw.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(sw, Dock.Left);
            row.Children.Add(sw);
            row.Children.Add(new TextBlock { Text = g.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            LoopRole role = g.Role;
            row.PointerEntered += (_, _) => HoverLoop(role);
            row.PointerExited += (_, _) => HoverLoop(null);
            ToolTip.SetTip(row, "Passe o mouse para acender no 3D.");
            PropsPanel.Children.Add(row);
        }
    }
}
