using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.App.Graphics;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Step;

namespace CncStudio.App.Modules;

/// <summary>
/// M3D: a peça em STEP dentro do Trabalho fresa. M3D-1: cartão "Peça pronta" com STEP (caminho, resumo e giros; o arquivo nunca é lido
/// ao abrir o trabalho) e a peça no 3D do Trabalho. M3D-2: clicar numa face da peça no 3D abre o menu da face (mesma regra do Modelo 3D,
/// <see cref="FaceMenuRules"/>) e a operação escolhida entra na árvore, vazia até o usuário digitar (<see cref="CamJobPart"/>).
/// </summary>
public partial class JobView
{
    /// <summary>Escolhe o STEP da peça e devolve o caminho no computador (null = cancelou ou sem caminho local).</summary>
    public Func<Task<string?>>? PickPartStep { get; set; }

    /// <summary>Modelo do STEP da peça lido nesta sessão: caminho, malha do arquivo e malha na posição (giros e topo). Nunca vai para o arquivo.</summary>
    private (string Path, TriangleMesh Raw, TriangleMesh Placed)? _partMesh;

    /// <summary>Faces e arestas da peça lidas da malha na posição (para o clique); null enquanto lê.</summary>
    private MeshFeatures? _partFeatures;

    private bool _partReading;

    /// <summary>Faces escolhidas no 3D (ids de <see cref="MeshFeatures.Faces"/>).</summary>
    private readonly HashSet<int> _partSelection = [];

    /// <summary>STEP-OPS S2: arestas escolhidas no 3D (contorno pela aresta). Não são desenhadas em cor: a contagem aparece no aviso.</summary>
    private readonly HashSet<int> _partEdges = [];

    private int _partAnalysis;

    private static readonly (string Label, ModelTurn Turn)[] PartTurns =
    [
        ("X +90°", ModelTurn.XPlus90), ("X −90°", ModelTurn.XMinus90), ("Y +90°", ModelTurn.YPlus90), ("Y −90°", ModelTurn.YMinus90), ("Virar", ModelTurn.Flip),
    ];

    private static string TurnName(ModelTurn t) => PartTurns.First(p => p.Turn == t).Label;

    /// <summary>Esquece a malha e a seleção (trabalho aberto ou peça trocada).</summary>
    private void ForgetPartMesh()
    {
        _partMesh = null;
        _partFeatures = null;
        ForgetHoles();
        _partSelection.Clear();
        _partAnalysis++;
    }

    /// <summary>Peça pela fonte escolhida: "Desenho DXF" ou "STEP da peça" (uma fonte só).</summary>
    private void PartSourceChoices(StackPanel card)
    {
        bool step = _job.PartStep is not null;
        card.Children.Add(Choices(
            Choice("Desenho DXF", !step, step ? () => UseDxfPart() : null, "A peça são os contornos do DXF (faixa Arquivo)."),
            Choice("STEP da peça", step, step ? null : () => _ = ImportPartStep(), "Abre a peça em STEP; as operações nascem das faces clicadas no 3D.")));
    }

    /// <summary>Volta para DXF: só sem operações na peça STEP (senão, trabalho novo).</summary>
    private void UseDxfPart()
    {
        if (CamJobPart.CannotSwitch(_job, toStep: false) is { } why)
        {
            Errors.Text = "• " + why;
            return;
        }

        _job.PartStep = null;
        _job.Contours.Clear();
        ForgetPartMesh();
        Refresh(keep: true);
    }

    /// <summary>Cartão da peça STEP: arquivo, motivo e "Localizar de novo" se sumiu, resumo, giros, topo em Z 0 e altura.</summary>
    private void PartStepCard(StackPanel card)
    {
        JobPartStep part = _job.PartStep!;
        var file = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var change = new Button { Content = "trocar", Padding = new Thickness(8, 2), FontSize = 11, IsEnabled = !_partReading };
        change.Click += async (_, _) => await ImportPartStep();
        DockPanel.SetDock(change, Dock.Right);
        file.Children.Add(change);
        file.Children.Add(new TextBlock { Text = CamJobPart.FileName(_job), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        ToolTip.SetTip(file, part.Path);
        card.Children.Add(file);

        if (part.FileProblem is not null)
        {
            card.Children.Add(UiKit.StatusNote("O arquivo da peça sumiu ou mudou de lugar. As operações já criadas continuam (os contornos ficam no trabalho); para clicar em faces, localize o STEP de novo.", "pending"));
            var locate = new Button { Content = "Localizar de novo", Padding = new Thickness(10, 4), FontSize = 12, IsEnabled = !_partReading };
            locate.Click += async (_, _) => await ImportPartStep();
            card.Children.Add(locate);
        }

        StepStockSummary b = part.Summary;
        card.Children.Add(Note($"Resumo: caixa {Fmt(b.SizeX)} × {Fmt(b.SizeY)} × {Fmt(b.SizeZ)} mm (X {Fmt(b.MinX)} a {Fmt(b.MaxX)}, Y {Fmt(b.MinY)} a {Fmt(b.MaxY)}, Z {Fmt(b.MinZ)} a {Fmt(b.MaxZ)}). O trabalho guarda o caminho, o resumo e os giros; o STEP só é lido para o 3D."));

        card.Children.Add(Caps("ORIENTAÇÃO"));
        card.Children.Add(Note(part.Turns.Count == 0 ? "Como veio no arquivo." : "Giros: " + string.Join(", ", part.Turns.Select(TurnName)) + "."));
        bool canTurn = _partMesh is not null && !_partReading && _job.Contours.Count == 0;
        var turns = new List<Control>();
        foreach ((string label, ModelTurn turn) in PartTurns)
        {
            turns.Add(Choice(label, false, canTurn ? () => TurnPart([.. part.Turns, turn], part.TopAtZero) : null));
        }

        turns.Add(Choice("Zerar giros", false, canTurn && part.Turns.Count > 0 ? () => TurnPart([], part.TopAtZero) : null));
        card.Children.Add(Choices([.. turns]));
        var top = new CheckBox { Content = "Topo da peça em Z 0 (como o DXF)", IsChecked = part.TopAtZero, IsEnabled = canTurn, FontSize = 12 };
        top.IsCheckedChanged += (_, _) => TurnPart(part.Turns, top.IsChecked == true);
        card.Children.Add(top);
        if (_job.Contours.Count > 0)
        {
            card.Children.Add(Note("Já há operações feitas nas faces: girar mudaria o lugar delas. Para girar, comece um trabalho novo."));
        }
        else if (_partMesh is null)
        {
            card.Children.Add(Note("Para girar, mostre a peça no 3D (lê o STEP)."));
        }

        if (_partMesh is null && part.FileProblem is null)
        {
            var show = new Button { Content = _partReading ? "Lendo o STEP…" : "Mostrar a peça no 3D (lê o STEP)", Padding = new Thickness(10, 4), FontSize = 12, IsEnabled = !_partReading };
            ToolTip.SetTip(show, "A leitura não trava a tela. Depois de ler, clique nas faces no 3D (botão 3D) para criar operações.");
            show.Click += async (_, _) => await LoadPartMesh(open3D: true);
            card.Children.Add(show);
        }
        else if (_partMesh is not null)
        {
            card.Children.Add(Note(_partFeatures is null
                ? "Lendo faces e furos da peça…"
                : "No 3D (botão 3D), clique numa face: fundo plano → Bolsão; furo vertical → Furação (Ctrl soma furos)."));
        }

        double height = b.SizeZ;
        var useHeight = new Button { Content = $"Usar a altura do STEP ({Fmt(height)} mm)", Padding = new Thickness(10, 4), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(useHeight, "Copia a altura da caixa da peça para o campo Altura da peça. O valor fica à vista e pode ser trocado.");
        useHeight.Click += (_, _) =>
        {
            _job.PartHeight = height;
            Refresh(keep: true);
        };
        card.Children.Add(useHeight);
    }

    /// <summary>Gira a peça (só sem contornos ainda) e refaz o resumo com a malha já lida.</summary>
    private void TurnPart(IReadOnlyList<ModelTurn> turns, bool topAtZero)
    {
        if (_partMesh is not { } m || _job.PartStep is not { } part || _job.Contours.Count > 0)
        {
            return;
        }

        _job.PartStep = CamJobPart.Import(part.Path, m.Raw, turns, topAtZero);
        SetPartMesh(part.Path, m.Raw);
        Refresh(keep: true);
    }

    /// <summary>Lê o STEP fora da tela (não trava).</summary>
    private static async Task<(TriangleMesh? Mesh, string? Error, double Seconds)> ReadPartStep(string path)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            StepResult r = await Task.Run(() => StepMeshReader.Read(File.ReadAllBytes(path)));
            return (r.Mesh, r.Error, clock.Elapsed.TotalSeconds);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, "não foi possível ler o arquivo (" + e.Message + ")", clock.Elapsed.TotalSeconds);
        }
    }

    /// <summary>Abre, troca ou localiza de novo o STEP da peça.</summary>
    private async Task ImportPartStep()
    {
        if (CamJobPart.CannotSwitch(_job, toStep: true) is { } why)
        {
            Errors.Text = "• " + why;
            return;
        }

        if (_partReading || PickPartStep is null || await PickPartStep() is not { } path)
        {
            return;
        }

        _partReading = true;
        Refresh(keep: true);
        Errors.Text = "• Lendo o STEP da peça… (a tela continua livre)";
        (TriangleMesh? raw, string? error, double seconds) = await ReadPartStep(path);
        _partReading = false;
        if (raw is null)
        {
            Refresh(keep: true);
            Errors.Text = "• STEP da peça não lido: " + error;
            return;
        }

        JobPartStep? before = _job.PartStep;
        if (before is not null && _job.Contours.Count > 0)
        {
            // localizar de novo com operações feitas: mantém os giros; o arquivo tem de ser a mesma peça (mesma caixa)
            JobPartStep again = CamJobPart.Import(path, raw, before.Turns, before.TopAtZero);
            if (!SameBox(again.Summary, before.Summary))
            {
                Refresh(keep: true);
                Errors.Text = "• Este STEP não é a mesma peça (a caixa não bate com o resumo). As operações foram feitas na peça antiga: use um trabalho novo para outra peça.";
                return;
            }

            _job.PartStep = again;
        }
        else
        {
            // uma fonte só: a peça STEP substitui o desenho DXF (sem operações, conferido acima)
            _job.Contours.Clear();
            _openChains = 0;
            _job.PartStep = CamJobPart.Import(path, raw, before?.Turns ?? [], before?.TopAtZero ?? true);
            if (before is null)
            {
                _job.Name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            }
        }

        // DESK-01: a altura da peça vem do STEP lido quando ainda está vazia (o campo continua editável)
        string height = "";
        if (_job.PartHeight is null && _job.PartStep is { Summary: { Valid: true } sum })
        {
            _job.PartHeight = Math.Round(sum.SizeZ, 3);
            height = $" Altura da peça: {_job.PartHeight.Value.ToString("0.###", Br)} mm (do STEP).";
        }

        SetPartMesh(path, raw);
        Refresh(keep: true);
        // DESK-01: abre o 3D já com a peça enquadrada
        View3DButton.IsChecked = true;
        Errors.Text = $"• STEP da peça lido em {seconds.ToString("0.0", Br)} s.{height} Clique nas faces no 3D para criar operações.";
    }

    /// <summary>Lê o STEP já escolhido só para o 3D. Se mudou desde que foi aberto (caixa diferente), não usa.</summary>
    private async Task LoadPartMesh(bool open3D = false)
    {
        if (_partReading || _job.PartStep is not { } part)
        {
            return;
        }

        _partReading = true;
        Refresh(keep: true);
        (TriangleMesh? raw, string? error, double seconds) = await ReadPartStep(part.Path);
        _partReading = false;
        string message;
        if (raw is null)
        {
            message = "• STEP da peça não lido: " + error;
        }
        else if (!SameBox(StepStockSummary.From(CamJobPart.Place(raw, part.Turns, part.TopAtZero)), part.Summary))
        {
            message = "• O STEP da peça mudou desde que foi aberto (a caixa não bate com o resumo). Use \"trocar\" num trabalho novo.";
        }
        else
        {
            if (_job.PartStep?.Path == part.Path)
            {
                SetPartMesh(part.Path, raw);
            }

            message = $"• STEP da peça lido em {seconds.ToString("0.0", Br)} s.";
            if (open3D)
            {
                // STEP-OPS: com peça STEP o 3D é a tela principal do Trabalho (a peça enquadrada, faces para clicar)
                View3DButton.IsChecked = true;
                message += " Clique nas faces ou arestas no 3D para criar operações.";
            }
        }

        Refresh(keep: true);
        Errors.Text = message;
    }

    /// <summary>Guarda a malha na posição e lê as faces em segundo plano (para o clique).</summary>
    private async void SetPartMesh(string path, TriangleMesh raw)
    {
        JobPartStep part = _job.PartStep!;
        TriangleMesh placed = CamJobPart.Place(raw, part.Turns, part.TopAtZero);
        _partMesh = (path, raw, placed);
        _partFeatures = null;
        ForgetHoles();
        _partSelection.Clear();
        _partEdges.Clear();
        int mine = ++_partAnalysis;
        ShowPartInScene();
        MeshFeatures features = await Task.Run(() => MeshFeatures.Analyze(placed));
        if (mine != _partAnalysis)
        {
            return; // trocou de peça ou girou no meio
        }

        _partFeatures = features;
        ReadHoles(features, placed);
        // STEP-OPS: operação cuja face não é achada neste STEP fica Incompleta até Reselecionar (salvar continua livre)
        int pending = CamJobFromGeometry.Reconcile(_job, features);
        Refresh(keep: true);
        if (pending > 0)
        {
            Errors.Text = $"• {pending} operação(ões) do 3D não acharam a face neste STEP e ficaram Incompletas: selecione a face no 3D e use Trocar seleção em Geometria (Reselecionar).";
        }
    }

    /// <summary>Peça e seleção no 3D do Trabalho.</summary>
    private void ShowPartInScene()
    {
        if (_partMesh is { } m && _job.PartStep?.Path == m.Path)
        {
            int[] chosen = _partFeatures is { } f ? [.. _partSelection.SelectMany(i => f.Faces[i].Triangles).Concat(SelectedOpTriangles()).Concat(HoverParts().Triangles).Distinct()] : [];
            // arestas escolhidas (e as da operação escolhida na árvore) acendem em laranja
            Scene3D.SetPart(m.Placed, chosen, HighlightedEdges());
            Scene3D.SetLoops([.. SceneLoops(), .. CompareFocusLoops(), .. HoleLoops()]);
        }
        else
        {
            Scene3D.SetPart(null, []);
            Scene3D.SetLoops([]);
        }
    }

    /// <summary>Clique no 3D: escolhe a face (Ctrl soma) e abre o menu da face perto do clique.</summary>
    private void OnPartClicked(object? sender, SceneClickEventArgs e)
    {
        if (_partFeatures is not { } features || _partMesh is not { } m)
        {
            return;
        }

        (MeshFace? face, MeshEdge? edge) = features.Pick(e.Origin, e.Direction, e.Tolerance);
        if (!e.Additive)
        {
            bool same = (face is not null && _partSelection.Count == 1 && _partEdges.Count == 0 && _partSelection.Contains(face.Id))
                || (edge is not null && _partEdges.Count == 1 && _partSelection.Count == 0 && _partEdges.Contains(edge.Id));
            _partSelection.Clear();
            _partEdges.Clear();
            if (same)
            {
                face = null;
                edge = null;
            }
        }

        if (face is not null && !_partSelection.Remove(face.Id))
        {
            _partSelection.Add(face.Id);
        }

        if (edge is not null && !_partEdges.Remove(edge.Id))
        {
            _partEdges.Add(edge.Id);
        }

        if (edge is not null)
        {
            Errors.Text = $"• {_partEdges.Count} aresta(s) escolhida(s) no 3D (Ctrl soma): use Contorno na faixa. Arestas vizinhas viram um laço só.";
            UpdatePalette();
        }

        ShowPartInScene();
        // STEP-CONT-01: a operação aberta no painel mostra Trocar seleção / + Adicionar conforme há seleção no 3D; refaz o painel agora
        if (SelectedOp() is { } selectedOp && selectedOp < _job.Operations.Count && _job.Operations[selectedOp].Geometry is not null)
        {
            BuildProps();
        }

        if (face is not null && _partSelection.Contains(face.Id))
        {
            ShowPartMenu(features, face);
        }
    }

    /// <summary>
    /// Menu da face (CAM-FACE-02, como os CAMs de mercado): fundo plano = Faceamento, Bolsão e Desbaste adaptativo; furo = Furação e
    /// Contorno por dentro; pino e parede reta = Contorno; parede inclinada = Chanfro e Contorno pela base. O que não serve fica
    /// apagado, com o motivo do Core.
    /// </summary>
    private void ShowPartMenu(MeshFeatures features, MeshFace face)
    {
        (string header, IReadOnlyList<JobFaceItem> items) = FaceMenuRules.ForJob(features, face, PartTopZ);
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = header, IsEnabled = false });
        foreach (JobFaceItem item in items)
        {
            IReadOnlyList<MeshFace> faces = item.Kind == JobOpKind.Drilling
                ? [.. _partSelection.Select(i => features.Faces[i]).Where(f => f.Kind == FaceKind.Hole)]
                : [face];
            string? why = CamJobFromGeometry.Unavailable(item.Kind, features, faces, [], PartTopZ, item.Side);
            var mi = new MenuItem { Header = why is null ? item.Label : item.Label + " — " + why, IsEnabled = why is null };
            if (why is not null)
            {
                ToolTip.SetTip(mi, why);
                ToolTip.SetShowOnDisabled(mi, true);
            }

            mi.Click += (_, _) => AddFromFace(item, face);
            flyout.Items.Add(mi);
        }

        flyout.ShowAt(Scene3D, showAtPointer: true);
    }

    /// <summary>Cria a operação escolhida no menu da face (CAM-FACE-02: também contorno, chanfro e desbaste adaptativo).</summary>
    private void AddFromFace(JobFaceItem item, MeshFace face)
    {
        if (_partFeatures is not { } features)
        {
            return;
        }

        Snap();
        int before = _job.Operations.Count;
        IReadOnlyList<MeshFace> faces = item.Kind == JobOpKind.Drilling
            ? [.. _partSelection.Select(i => features.Faces[i]).Where(f => f.Kind == FaceKind.Hole)]
            : [face];
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(_job, item.Kind, features, faces, [], PartTopZ, item.Side);
        if (error is not null)
        {
            Errors.Text = "• " + error;
            return;
        }

        if (item.Adaptive)
        {
            foreach (int k in made)
            {
                _job.Operations[k] = CamPalette.AsAdaptive(_job.Operations[k]);
            }
        }

        MarkNewOps(before);
        _partSelection.Clear();
        Refresh(op: made.Count > 0 ? made[0] : -1);
        ShowPartInScene();
        string note = item.Kind switch
        {
            JobOpKind.Contour when _job.Operations[made[0]].Label == "Contorno pela base" =>
                "Contorno pela base da parede inclinada: sai em degraus de Z e deixa a escada de material (Conferir no peça × bruto). ",
            JobOpKind.Contour or JobOpKind.Chamfer => "Topo e fundo lidos da parede no 3D: confira. ",
            _ => "",
        };
        Errors.Text = "• Operação criada na árvore. " + note + "Digite T, Ø, rotação e avanços. " + CamJobPart.MeshContourNote;
    }
}
