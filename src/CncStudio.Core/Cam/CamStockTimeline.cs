using System.Diagnostics;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Cam;

/// <summary>Estado do bruto depois de uma operação do trabalho (CAM-STOCK-01).</summary>
public enum CamStockStatus
{
    /// <summary>Simulada: há o bruto depois dela.</summary>
    Ready,

    /// <summary>Operação desligada: o bruto passa igual.</summary>
    Disabled,

    /// <summary>Sem estado: a operação (ou uma anterior) não gera, ou o bruto, o zero ou o motor não permitem calcular.</summary>
    NoState,
}

/// <summary>Bruto depois de uma operação: o estado, o motivo quando não há, o volume tirado e o instantâneo do material.</summary>
public sealed class CamStockState
{
    internal CamStockState(int index, CamStockStatus status, string? reason)
    {
        Index = index;
        Status = status;
        Reason = reason;
    }

    /// <summary>Posição da operação no trabalho (0 = primeira).</summary>
    public int Index { get; }

    public CamStockStatus Status { get; }

    /// <summary>Motivo em pt-BR quando <see cref="Status"/> é <see cref="CamStockStatus.NoState"/>.</summary>
    public string? Reason { get; }

    /// <summary>Volume tirado por esta operação (mm³), na resolução da grade.</summary>
    public double RemovedVolume { get; internal set; }

    /// <summary>Volume que sobrou depois dela (mm³).</summary>
    public double Volume { get; internal set; }

    /// <summary>Tempo do cálculo desta operação (zero quando reaproveitada).</summary>
    public TimeSpan Elapsed { get; internal set; }

    /// <summary>True quando veio do cálculo anterior sem simular de novo.</summary>
    public bool Reused { get; internal set; }

    /// <summary>Percurso simulado da operação (null sem estado ou desligada).</summary>
    public ToolPath? Path { get; internal set; }

    /// <summary>CAM-AR-01: "começar no material" desta operação (null quando ela não pede).</summary>
    public StockStart? Start { get; internal set; }

    /// <summary>CAM-AR-01: aviso Conferir de "começar no material" (ajustada ou mantida no topo digitado); null quando não pede.</summary>
    public string? StartNote { get; internal set; }

    /// <summary>
    /// CAM-RAPIDO-01: rápidos (G0) desta operação que tiram material do bruto que sobrou das anteriores (a mesma conta do simulador
    /// do Code). Zero = nenhum. Colisão provável na máquina: ordem das operações ou alturas a conferir.
    /// </summary>
    public int RapidHits { get; internal set; }

    /// <summary>CAM-RAPIDO-01: ponto final do primeiro rápido no material, em coordenadas do programa (null sem rápido no material).</summary>
    public Point3? RapidPoint { get; internal set; }

    /// <summary>CAM-RAPIDO-01: texto do Erro do console (null sem rápido no material).</summary>
    public string? RapidNote => RapidHits == 0 || RapidPoint is not { } p ? null
        : $"Operação {Index + 1}: {RapidHits} {(RapidHits == 1 ? "rápido (G0) entra" : "rápidos (G0) entram")} no material que as operações anteriores ainda não tiraram (ex.: X{p.X:0.###} Y{p.Y:0.###} Z{p.Z:0.###}). Colisão provável: confira a ordem das operações ou as alturas (verificação, nunca garantia).";

    internal string? Key { get; set; }

    internal HeightMap.Snapshot? MapSnapshot { get; set; }

    internal ulong[]? VoxelSnapshot { get; set; }
}

/// <summary>
/// Bruto atualizado a cada operação de fresa do trabalho (CAM-STOCK-01): monta o bruto do trabalho (não do comentário BRUTO),
/// gera o programa de cada operação sozinha, simula sobre o bruto da anterior com o mesmo motor do Code (grade de alturas,
/// ou voxels com eixo rotativo e cilindro deitado) e guarda um instantâneo por operação. Nada é gravado: é recalculado.
/// Simulação é verificação, nunca garantia. Não é thread-safe: chamar numa tarefa só.
/// </summary>
public sealed class CamStockTimeline
{
    // Programa nº e G5x só do texto interno de cada operação: não mudam a geometria e nunca aparecem nem são salvos
    private const int InternalProgramNumber = 1;
    private const int InternalWorkOffset = 54;

    private readonly HeightMap.Snapshot? _initialMap;
    private readonly ulong[]? _initialVoxels;

    private CamStockTimeline(IReadOnlyList<CamStockState> states, string? problem, HeightMap? map, VoxelStock? voxels, string stockKey,
        Func<int, ToolProfile> profiles, double initialVolume, IReadOnlyList<JobOpResult>? items = null)
    {
        Items = items ?? [];
        States = states;
        Problem = problem;
        Map = map;
        Voxels = voxels;
        StockKey = stockKey;
        Profiles = profiles;
        InitialVolume = initialVolume;
        _initialMap = map?.TakeSnapshot();
        _initialVoxels = voxels?.TakeSnapshot();
    }

    /// <summary>
    /// CAM-AR-01: percurso de cada operação como foi simulado (já com "começar no material"); vazio quando não houve bruto. É o que o
    /// <see cref="CamJobBuilder.Build"/> usa no programa quando alguma operação começa no material.
    /// </summary>
    public IReadOnlyList<JobOpResult> Items { get; }

    /// <summary>CAM-AR-01: avisos Conferir de "começar no material", na ordem das operações.</summary>
    public IReadOnlyList<string> StartNotes => [.. States.Select(s => s.StartNote).OfType<string>()];

    /// <summary>CAM-RAPIDO-01: Erros de rápido no material, um por operação.</summary>
    public IReadOnlyList<string> RapidNotes => [.. States.Select(s => s.RapidNote).OfType<string>()];

    /// <summary>Um estado por operação do trabalho, na mesma ordem.</summary>
    public IReadOnlyList<CamStockState> States { get; }

    /// <summary>Motivo comum a todas as operações sem estado (bruto, zero); null quando o bruto foi montado.</summary>
    public string? Problem { get; }

    /// <summary>Material na grade de alturas (fresa 3 eixos); null com voxels ou sem bruto.</summary>
    public HeightMap? Map { get; }

    /// <summary>Material em voxels (eixo rotativo ou cilindro deitado); null na grade de alturas ou sem bruto.</summary>
    public VoxelStock? Voxels { get; }

    public bool UsesVoxels => Voxels is not null;

    /// <summary>Perfil de cada ferramenta usado no corte (para desenhar a ferramenta igual).</summary>
    public Func<int, ToolProfile> Profiles { get; }

    /// <summary>Volume do bruto inteiro (mm³).</summary>
    public double InitialVolume { get; }

    /// <summary>Quantas operações foram simuladas neste cálculo (as reaproveitadas não contam).</summary>
    public int Simulated => States.Count(s => s.Status == CamStockStatus.Ready && !s.Reused);

    internal string StockKey { get; }

    /// <summary>
    /// Põe no material (<see cref="Map"/> ou <see cref="Voxels"/>) o bruto depois da operação <paramref name="index"/>
    /// (−1 = bruto inteiro). Operação desligada mostra o bruto da última com estado antes dela. False sem estado.
    /// </summary>
    public bool ShowAfter(int index)
    {
        if (Map is null && Voxels is null)
        {
            return false;
        }

        if (index < 0)
        {
            Apply(_initialMap, _initialVoxels);
            return true;
        }

        if (index >= States.Count || States[index].Status == CamStockStatus.NoState)
        {
            return false;
        }

        for (int i = index; i >= 0; i--)
        {
            if (States[i].Status == CamStockStatus.Ready)
            {
                Apply(States[i].MapSnapshot, States[i].VoxelSnapshot);
                return true;
            }
        }

        Apply(_initialMap, _initialVoxels);
        return true;
    }

    private void Apply(HeightMap.Snapshot? map, ulong[]? voxels)
    {
        if (Map is not null && map is not null)
        {
            Map.Restore(map);
        }

        if (Voxels is not null && voxels is not null)
        {
            Voxels.Restore(voxels);
        }
    }

    /// <summary>
    /// Calcula o bruto depois de cada operação. <paramref name="previous"/>: cálculo anterior do mesmo trabalho; os estados
    /// se repetem enquanto o bruto inicial e o programa de cada operação forem iguais, e o cálculo recomeça na primeira diferença.
    /// </summary>
    public static CamStockTimeline Compute(CamJob job, MachineProfile? machine, ToolBank? bank = null,
        int maxCellsPerSide = SimulationFactory.DesktopCells, CamStockTimeline? previous = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        Func<int, ToolProfile> flat = tool => ToolProfile.Flat(ProgramAnalysis.DefaultToolDiameter);
        (string? problem, StockSolid? solid, Point3 zero) = StockOf(job);
        if (problem is not null)
        {
            return Empty(job, problem, flat);
        }

        bool voxels = NeedsVoxels(job, solid!);
        (HeightMap? map, VoxelStock? stock) = Material(solid!, zero, voxels, maxCellsPerSide);
        string stockKey = $"{(voxels ? "voxel" : "mapa")}|{maxCellsPerSide}|{solid}|{zero}";

        // perfil das ferramentas como o pacote do CAM leva ao Code (tipo, raio de canto, ângulo); sem banco, topo reto
        CamHandoff handoff = CamHandoff.Create(job, "", machine, bank);
        Func<int, double, ToolProfile?>? package = CamHandoffCode.PackageProfiles(handoff);
        // CAM-AR-01: só confia no bruto simulado quando a forma de cada ferramenta é conhecida pelo banco (topo reto suposto
        // tiraria material demais com fresa de ponta redonda: o bruto simulado ficaria mais baixo que o real)
        HashSet<int> knownShape = [.. handoff.Tools.Where(t => t.Type is { } code && BankToolTypes.FromCode(code) is not null).Select(t => t.Number)];
        var cutBy = new List<int>();
        Dialect dialect = job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc;
        // CAM-ACHADOS-01: o Ø de cada T é o da operação (como o pacote leva ao Code), não o do comentário da troca; sem isso uma
        // broca cujo nome não traz "D9" virava a fresa padrão Ø10 no bruto e a peça × bruto acusava gouge falso no furo
        MachineSettings settings = CamHandoffCode.ApplyDiameters(
            MachineSettings.Default with { Machine = MachineKind.Mill, PartFrame = RotaryPartFrame.From(machine) }, handoff);
        var options = new PostOptions(job.Dialect, job.Name, InternalProgramNumber, InternalWorkOffset, job.Coolant, machine);

        var built = new List<JobOpResult>(CamJobBuilder.BuildOperations(job, machine));
        HeightMap.Snapshot? initialMap = map?.TakeSnapshot();
        var states = new List<CamStockState>(built.Count);
        double initialVolume = VolumeOf(map, stock);
        double volume = initialVolume;
        bool reuse = previous is not null && previous.StockKey == stockKey && previous.Problem is null;
        Func<int, double> lastDiameters = _ => ProgramAnalysis.DefaultToolDiameter;
        int? blockedBy = null;
        (StockStart? Start, string? Note) startOf;
        for (int i = 0; i < built.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JobOpResult item = built[i];
            if (blockedBy is { } b)
            {
                states.Add(new CamStockState(i, CamStockStatus.NoState, $"Operação {b + 1} anterior sem estado.") { Volume = volume });
                reuse = false;
                continue;
            }

            if (item.Skipped)
            {
                var off = new CamStockState(i, CamStockStatus.Disabled, null) { Volume = volume, Key = "desligada" };
                reuse = reuse && Same(previous!, i, off.Key);
                states.Add(off);
                continue;
            }

            if (!item.Ok)
            {
                states.Add(new CamStockState(i, CamStockStatus.NoState, item.Error ?? "A operação não gera.") { Volume = volume });
                blockedBy = i;
                reuse = false;
                continue;
            }

            if (job.Operations[i].TopFromStock)
            {
                (item, StockStart? start, string? note) = StartInMaterial(job, machine, i, item, map, initialMap, states, knownShape, cutBy, zero);
                built[i] = item;
                startOf = (start, note);
            }
            else
            {
                startOf = (null, null);
            }

            cutBy.Add(job.Operations[i].Tool.Number);
            IReadOnlyList<CamOperation> placed = CamJobZero.Translate([item.Result!.Operation!], zero);
            string text = CamPost.Generate(placed, options);
            CamStockState? before = reuse && Same(previous!, i, text) ? previous!.States[i] : null;
            if (before is not null)
            {
                var again = new CamStockState(i, CamStockStatus.Ready, null)
                {
                    Key = text, MapSnapshot = before.MapSnapshot, VoxelSnapshot = before.VoxelSnapshot, Path = before.Path,
                    RemovedVolume = before.RemovedVolume, Volume = before.Volume, Reused = true, Start = startOf.Start, StartNote = startOf.Note,
                    RapidHits = before.RapidHits, RapidPoint = before.RapidPoint,
                };
                volume = before.Volume;
                states.Add(again);
                continue;
            }

            reuse = false;
            var watch = Stopwatch.StartNew();
            ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, dialect, settings, cancellationToken);
            ToolPath path = analysis.ToolPath;
            lastDiameters = analysis.ToolDiameterOf;
            Func<int, ToolProfile> profiles = SimulationFactory.MillProfiles(null, analysis.ToolDiameterOf, package);
            CamStockState? last = LastReady(states);
            IMaterialSimulation simulation = stock is not null
                ? new VoxelSimulator(stock, path, profiles)
                : new MaterialSimulator(map!, path.Moves, profiles, SimulationFactory.RotatedMoveSkipper(path));

            // o construtor do simulador volta ao bruto inteiro: recoloca o bruto da operação anterior
            if (last is not null)
            {
                if (map is not null)
                {
                    map.Restore(last.MapSnapshot!);
                }
                else
                {
                    stock!.Restore(last.VoxelSnapshot!);
                }
            }

            simulation.RunToEnd();
            double now = VolumeOf(map, stock);
            IReadOnlyList<int> rapids = simulation.RapidCollisions;
            Point3? rapidPoint = rapids.Count > 0 && rapids[0] < path.Moves.Count
                ? path.Moves[rapids[0]].End : null;
            states.Add(new CamStockState(i, CamStockStatus.Ready, null)
            {
                Key = text, Path = path, MapSnapshot = map?.TakeSnapshot(), VoxelSnapshot = stock?.TakeSnapshot(),
                RemovedVolume = Math.Max(0, volume - now), Volume = now, Elapsed = watch.Elapsed, Start = startOf.Start, StartNote = startOf.Note,
                RapidHits = rapids.Count, RapidPoint = rapidPoint,
            });
            volume = now;
        }

        Func<int, ToolProfile> drawProfiles = SimulationFactory.MillProfiles(null, lastDiameters, package);
        var timeline = new CamStockTimeline(states, null, map, stock, stockKey, drawProfiles, initialVolume, built);
        return timeline;
    }

    /// <summary>
    /// CAM-AR-01: mede o material que sobrou na área da operação (bruto depois da última com estado) e gera de novo com as alturas
    /// ajustadas. Qualquer dúvida (voxel, forma de ferramenta desconhecida, tipo que não usa, sem material) mantém o topo digitado.
    /// </summary>
    private static (JobOpResult Item, StockStart? Start, string? Note) StartInMaterial(CamJob job, MachineProfile? machine, int i, JobOpResult item,
        HeightMap? map, HeightMap.Snapshot? initialMap, List<CamStockState> states, HashSet<int> knownShape, List<int> cutBy, Point3 zero)
    {
        CamOperationSpec spec = job.Operations[i];
        if (!CamStockStart.Applies(spec.Kind))
        {
            return (item, null, CamStockStart.KeptNote(i, "só Faceamento, Contorno e Bolsão"));
        }

        if (map is null)
        {
            return (item, null, CamStockStart.KeptNote(i, "bruto em voxel: eixo rotativo ou cilindro deitado"));
        }

        foreach (int t in cutBy.Append(spec.Tool.Number))
        {
            if (!knownShape.Contains(t))
            {
                return (item, null, CamStockStart.KeptNote(i, $"forma da ferramenta T{t} desconhecida: escolha-a no banco"));
            }
        }

        CamStockState? last = LastReady(states);
        map.Restore(last?.MapSnapshot ?? initialMap!);
        double? top = CamStockStart.MaterialTop(map, item.Result!.Operation!, zero, spec.Levels.SafeZ);
        StockStart start = CamStockStart.Adjust(spec.Levels, top, CamStockStart.ClearanceFor(map)) with { FromOperation = last?.Index ?? -1 };
        if (!start.Adjusted)
        {
            // CAM-ACHADOS-01: material acima do topo digitado também diz o motivo (antes ficava sem texto e a tela dizia só "não aplicado")
            return start.Reason is { } why ? (item, start, CamStockStart.KeptNote(i, why))
                : CamStockStart.AboveReason(spec.Levels, start) is { } above ? (item, start, CamStockStart.KeptNote(i, above))
                : (item, start, null);
        }

        OperationResult again = CamJobBuilder.BuildOne(job, spec with { Levels = start.Levels }, machine);
        return again.Ok
            ? (new JobOpResult(i, false, again), start, CamStockStart.AdjustedNote(i, start))
            : (item, start, CamStockStart.KeptNote(i, again.Error ?? "não gerou com as alturas novas"));
    }

    private static CamStockTimeline Empty(CamJob job, string problem, Func<int, ToolProfile> profiles)
    {
        var states = new List<CamStockState>(job.Operations.Count);
        for (int i = 0; i < job.Operations.Count; i++)
        {
            states.Add(new CamStockState(i, CamStockStatus.NoState, problem));
        }

        return new CamStockTimeline(states, problem, null, null, "", profiles, 0);
    }

    private static bool Same(CamStockTimeline previous, int index, string key) =>
        index < previous.States.Count && previous.States[index].Key == key;

    private static CamStockState? LastReady(List<CamStockState> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            if (states[i].Status == CamStockStatus.Ready)
            {
                return states[i];
            }
        }

        return null;
    }

    /// <summary>Bruto do trabalho e zero; ou o motivo de não haver bruto por operação.</summary>
    private static (string? Problem, StockSolid? Solid, Point3 Zero) StockOf(CamJob job)
    {
        StockCheck check = CamJobStock.Check(job);
        if (check.NotDefined)
        {
            return (CamJobInfo.StockMissing + ".", null, default);
        }

        if (CamJobStock.IncompleteReason(check) is { } why)
        {
            return (CamJobStock.IncompletePrefix + ": " + why + ".", null, default);
        }

        if (check.Solid is not { } solid)
        {
            return (CamJobStock.IncompletePrefix + ".", null, default);
        }

        if (solid.Kind == StockSolidKind.Step)
        {
            return ("Bruto STEP: o bruto por operação ainda não usa bruto STEP.", null, default);
        }

        if (CamJobZero.Position(job) is not { } zero)
        {
            return ("Zero da peça incompleto: sem ele não há bruto por operação.", null, default);
        }

        return (null, solid, zero);
    }

    /// <summary>Voxels se alguma operação ligada gira a peça (face girada, texto no cilindro) ou se o bruto redondo está deitado.</summary>
    internal static bool NeedsVoxels(CamJob job, StockSolid solid) =>
        job.Operations.Any(s => s.Enabled && (s.Face is not null || s.Kind == JobOpKind.WrapText))
        || (solid.Kind is StockSolidKind.Cylinder or StockSolidKind.Tube && solid.Axis != StockAxis.Z);

    private static (HeightMap? Map, VoxelStock? Voxels) Material(StockSolid s, Point3 zero, bool voxels, int maxCellsPerSide)
    {
        // referencial do programa: o sólido do trabalho menos o zero (a mesma conta que desloca as operações)
        double x0 = s.MinX - zero.X, x1 = s.MaxX - zero.X, y0 = s.MinY - zero.Y, y1 = s.MaxY - zero.Y, z0 = s.MinZ - zero.Z, z1 = s.MaxZ - zero.Z;
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, cz = (z0 + z1) / 2;
        double outer = s.Diameter / 2, inner = s.Kind == StockSolidKind.Tube ? s.InnerDiameter / 2 : 0;
        bool round = s.Kind is StockSolidKind.Cylinder or StockSolidKind.Tube;
        if (voxels)
        {
            Bounds3 box = Bounds3.Empty.Include(new Point3(x0, y0, z0)).Include(new Point3(x1, y1, z1));
            Func<double, double, double, bool>? shape = null;
            if (round)
            {
                StockAxis axis = s.Axis;
                shape = (x, y, z) =>
                {
                    (double u, double v) = axis switch { StockAxis.X => (y - cy, z - cz), StockAxis.Y => (x - cx, z - cz), _ => (x - cx, y - cy) };
                    double d = Math.Sqrt((u * u) + (v * v));
                    return d <= outer && d >= inner;
                };
            }

            (int perSide, long total) = SimulationFactory.VoxelLimits(maxCellsPerSide);
            return (null, new VoxelStock(box, VoxelStock.ChooseVoxelSize(box, perSide, total), shape));
        }

        HeightMap map = HeightMap.CreateForStock(Stock.Block(x0, x1, y0, y1, z0, z1), maxCellsPerSide);
        if (round)
        {
            map.SetMaterialShape((x, y) =>
            {
                double d = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                return d <= outer && d >= inner;
            });
        }

        return (map, null);
    }

    private static double VolumeOf(HeightMap? map, VoxelStock? voxels)
    {
        if (voxels is not null)
        {
            return voxels.Volume;
        }

        if (map is null)
        {
            return 0;
        }

        double sum = 0;
        ReadOnlySpan<float> heights = map.Heights;
        for (int row = 0; row < map.Rows; row++)
        {
            for (int column = 0; column < map.Columns; column++)
            {
                if (!map.IsEmptyCell(column, row))
                {
                    sum += Math.Max(0, heights[(row * map.Columns) + column] - map.BottomZ);
                }
            }
        }

        return sum * map.CellSize * map.CellSize;
    }
}
