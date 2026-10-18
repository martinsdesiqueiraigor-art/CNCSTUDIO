using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Scene;

/// <summary>Uma malha da cena com o papel dela e um deslocamento (a ferramenta anda; o resto fica parado).</summary>
public sealed class SceneObject
{
    internal SceneObject(string id, SceneRole role, SolidMesh mesh)
    {
        Id = id;
        Role = role;
        Mesh = mesh;
    }

    /// <summary>Nome estável (ex.: "material/3", "ferramenta/corte"): o motor 3D usa para achar o que trocar.</summary>
    public string Id { get; }

    public SceneRole Role { get; }

    public SolidMesh Mesh { get; private set; }

    /// <summary>Deslocamento aplicado à malha inteira (posição da ponta da ferramenta).</summary>
    public Point3 Offset { get; internal set; }

    public bool Visible { get; set; } = true;

    /// <summary>Muda quando a malha é trocada: o motor reenvia a malha à placa de vídeo.</summary>
    public int Version { get; private set; }

    internal void Replace(SolidMesh mesh)
    {
        Mesh = mesh;
        Version++;
    }
}

/// <summary>
/// O que a simulação 3D deve mostrar, sem saber como: material, fixações, ferramenta e
/// trajetória, com cores por papel e a câmera. Web e desktop só desenham isto.
/// O material da fresa vem em blocos (<see cref="HeightMapMesh"/>), e <see cref="Refresh"/>
/// troca só os blocos que mudaram.
/// </summary>
public sealed class SimulationScene
{
    private readonly List<SceneObject> _objects = [];
    private readonly List<SceneLines> _lines = [];
    private readonly Dictionary<int, (SolidMesh Cutter, SolidMesh Holder)> _toolMeshes = [];
    private readonly Func<int, ToolProfile> _profileOf;
    private readonly MachiningSetup? _setup;
    private readonly ToolPath _path;
    private readonly HeightMapMesh? _mill;
    private readonly TurningStock? _turning;
    private readonly SceneObject _cutter;
    private readonly SceneObject _holder;
    private ScenePalette _palette;
    private bool _quarterSection = true;
    private int _turningVersion = -1;
    private int _tool = -1;

    private SimulationScene(ToolPath path, Func<int, ToolProfile> profileOf, MachiningSetup? setup, ScenePalette palette,
        HeightMapMesh? mill, TurningStock? turning)
    {
        _path = path;
        _profileOf = profileOf;
        _setup = setup;
        _palette = palette;
        _mill = mill;
        _turning = turning;
        _cutter = new SceneObject("ferramenta/corte", SceneRole.Tool, SolidMesh.Create(1));
        _holder = new SceneObject("ferramenta/suporte", SceneRole.Holder, SolidMesh.Create(1));
        BuildStatic();
        SetRapidCollisions([]);
        Refresh();
    }

    /// <summary>Cena da fresa: material do <paramref name="map"/>, fixações do setup e a trajetória.</summary>
    public static SimulationScene ForMill(HeightMap map, ToolPath path, Func<int, ToolProfile> profileOf,
        MachiningSetup? setup = null, ScenePalette? palette = null)
    {
        palette ??= ScenePalette.Realistic;
        return new SimulationScene(path, profileOf, setup, palette, new HeightMapMesh(map, palette), null);
    }

    /// <summary>Cena do torno: peça de revolução (com corte de 1/4 opcional) e o inserto.</summary>
    public static SimulationScene ForLathe(TurningStock stock, ToolPath path, MachiningSetup? setup = null, ScenePalette? palette = null)
    {
        return new SimulationScene(path, _ => ToolProfile.Flat(1), setup, palette ?? ScenePalette.Realistic, null, stock);
    }

    public bool IsLathe => _turning is not null;

    public IReadOnlyList<SceneObject> Objects => _objects;

    public IReadOnlyList<SceneLines> Lines => _lines;

    /// <summary>Material da fresa em blocos (null no torno).</summary>
    public HeightMapMesh? MillMaterial => _mill;

    /// <summary>Câmera atual; a tela troca ao girar, aproximar ou escolher uma vista.</summary>
    public SceneCamera Camera { get; set; } = new(Point3.Origin, 100, 35, 30);

    /// <summary>Cores (metal ou modo técnico). Trocar refaz as malhas.</summary>
    public ScenePalette Palette
    {
        get => _palette;
        set
        {
            if (ReferenceEquals(value, _palette))
            {
                return;
            }

            _palette = value;
            _toolMeshes.Clear();
            int tool = _tool;
            _tool = -1;
            BuildStatic();
            if (_mill is not null)
            {
                _mill.Palette = value;
            }

            _turningVersion = -1;
            Refresh();
            if (tool >= 0)
            {
                SetTool(tool, _cutter.Offset);
            }
        }
    }

    /// <summary>Torno: mostra a peça com 1/4 removido (vê o perfil e o furo por dentro).</summary>
    public bool QuarterSection
    {
        get => _quarterSection;
        set
        {
            if (value != _quarterSection)
            {
                _quarterSection = value;
                _turningVersion = -1;
                Refresh();
            }
        }
    }

    /// <summary>Caixa de tudo o que está parado na cena (material e fixações), para o "ajustar".</summary>
    public Bounds3 Bounds
    {
        get
        {
            Bounds3 box = Bounds3.Empty;
            foreach (SceneObject o in _objects)
            {
                if (o.Visible && o.Role is not (SceneRole.Tool or SceneRole.Holder) && o.Mesh.VertexCount > 0)
                {
                    Bounds3 b = o.Mesh.Bounds;
                    box = box.Include(b.Min + o.Offset).Include(b.Max + o.Offset);
                }
            }

            return box;
        }
    }

    /// <summary>Enquadra a cena inteira na vista dada.</summary>
    public void Fit(SceneView view, double aspect) => Camera = SceneCamera.Fit(Bounds, view, aspect, Camera.FieldOfView);

    /// <summary>
    /// Atualiza o material depois de a simulação avançar e devolve os objetos cuja malha mudou
    /// (o motor 3D reenvia só esses).
    /// </summary>
    public IReadOnlyList<SceneObject> Refresh()
    {
        var changed = new List<SceneObject>();
        if (_mill is not null)
        {
            foreach (int index in _mill.Update())
            {
                SceneObject tile = _objects.First(o => o.Id == "material/" + index);
                tile.Replace(_mill.Tile(index));
                changed.Add(tile);
            }
        }
        else if (_turning is not null && _turning.Version != _turningVersion)
        {
            _turningVersion = _turning.Version;
            SceneObject part = _objects.First(o => o.Id == "material/0");
            part.Replace(SceneMeshes.Turning(_turning, _quarterSection, _palette));
            changed.Add(part);
        }

        return changed;
    }

    /// <summary>
    /// Põe a ferramenta "tool" com a ponta em "tip" (no torno, (X = raio, Z) da ponta do inserto).
    /// Troca a malha só quando o número da ferramenta muda.
    /// </summary>
    public void SetTool(int tool, Point3 tip)
    {
        if (tool != _tool)
        {
            _tool = tool;
            if (!_toolMeshes.TryGetValue(tool, out (SolidMesh Cutter, SolidMesh Holder) meshes))
            {
                meshes = _turning is not null
                    ? SceneMeshes.LatheTool(_palette)
                    : SceneMeshes.MillTool(_profileOf(tool) ?? ToolProfile.Flat(10), _setup?.Tools.FirstOrDefault(t => t.Number == tool), _palette);
                _toolMeshes[tool] = meshes;
            }

            _cutter.Replace(meshes.Cutter);
            _holder.Replace(meshes.Holder);
        }

        _cutter.Offset = tip;
        _holder.Offset = tip;
    }

    /// <summary>Refaz as linhas da trajetória com os G00 que encostaram no material destacados.</summary>
    public void SetRapidCollisions(IReadOnlyCollection<int> rapidMoves)
    {
        (SceneLines feed, SceneLines rapid, SceneLines hit) = SceneLines.FromToolPath(_path, rapidMoves);
        _lines.Clear();
        _lines.Add(feed);
        _lines.Add(rapid);
        _lines.Add(hit);
    }

    // Material (vazio até o Refresh), fixações e ferramenta
    private void BuildStatic()
    {
        Dictionary<string, bool> visible = _objects.ToDictionary(o => o.Id, o => o.Visible);
        _objects.Clear();

        if (_mill is not null)
        {
            for (int i = 0; i < _mill.TileCount; i++)
            {
                _objects.Add(new SceneObject("material/" + i, SceneRole.Material, _mill.Tile(i)));
            }
        }
        else
        {
            _objects.Add(new SceneObject("material/0", SceneRole.Material, SolidMesh.Create(1)));
        }

        if (_setup is not null)
        {
            for (int i = 0; i < _setup.Fixtures.Count; i++)
            {
                (SolidMesh body, SolidMesh jaws) = SceneMeshes.Fixture(_setup.Fixtures[i], _setup.Stock, _palette);
                _objects.Add(new SceneObject($"fixacao/{i}/base", SceneRole.Fixture, body));
                if (jaws.VertexCount > 0)
                {
                    _objects.Add(new SceneObject($"fixacao/{i}/mordentes", SceneRole.FixtureJaw, jaws));
                }
            }
        }

        _objects.Add(_cutter);
        _objects.Add(_holder);
        foreach (SceneObject o in _objects)
        {
            if (visible.TryGetValue(o.Id, out bool v))
            {
                o.Visible = v;
            }
        }
    }
}
