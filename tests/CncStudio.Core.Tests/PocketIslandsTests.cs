using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class PocketIslandsTests
{
    private static readonly CamTool Tool = new(1, 6, 3000, 600, 200);
    private static readonly CamLevels Levels = new(50, 2, 0, -4, 2);
    private static readonly PocketParams Params = new(60, 0, 2);

    private static IEnumerable<(double X, double Y)> CuttingXY(CamOperation op) =>
        op.Moves.Where(m => m.Kind != MoveKind.Rapid && m.Z <= 0.0001).Select(m => (m.X, m.Y));

    [Fact]
    public void Acha_ilhas_so_dentro_do_contorno()
    {
        Contour box = Contour.Rectangle(0, 0, 80, 60);
        Contour inside = Contour.Circle(new Point2(40, 30), 8);
        Contour outside = Contour.Circle(new Point2(120, 30), 8);
        IReadOnlyList<Contour> found = CamOperations.FindIslands(box, [box, inside, outside]);
        Assert.Single(found);
        Assert.Same(inside, found[0]);
    }

    [Fact]
    public void Sem_ilhas_e_igual_ao_bolsao_comum()
    {
        Contour box = Contour.Rectangle(0, 0, 80, 60);
        OperationResult a = CamOperations.PocketWithIslands(box, [], Tool, Levels, Params);
        OperationResult b = CamOperations.Pocket(box, Tool, Levels, Params);
        Assert.True(a.Ok);
        Assert.Equal(b.Operation!.Moves.Count, a.Operation!.Moves.Count);
    }

    [Fact]
    public void Ferramenta_nunca_entra_na_ilha_nem_sai_da_parede()
    {
        Contour box = Contour.Rectangle(0, 0, 80, 60);
        var island = Contour.Circle(new Point2(40, 30), 10);
        OperationResult r = CamOperations.PocketWithIslands(box, [island], Tool, Levels, Params);
        Assert.True(r.Ok, r.Error);
        // Centro da ferramenta ao menos a (raio da ilha + raio da ferramenta) do centro da ilha
        double minAllowed = 10 + Tool.Radius - 0.05;
        foreach ((double x, double y) in CuttingXY(r.Operation!))
        {
            Assert.True(Math.Sqrt(((x - 40) * (x - 40)) + ((y - 30) * (y - 30))) >= minAllowed, $"({x:0.###},{y:0.###}) entra na ilha");
            Assert.InRange(x, Tool.Radius - 0.01, 80 - Tool.Radius + 0.01);
            Assert.InRange(y, Tool.Radius - 0.01, 60 - Tool.Radius + 0.01);
        }

        Assert.Contains(r.Operation!.Warnings, w => w.Contains("ilhas"));
    }

    [Fact]
    public void Passes_paralelos_cobrem_os_dois_lados_da_ilha()
    {
        Contour box = Contour.Rectangle(0, 0, 80, 60);
        var island = Contour.Circle(new Point2(40, 30), 10);
        CamOperation op = CamOperations.PocketWithIslands(box, [island], Tool, Levels, Params).Operation!;
        var linePoints = CuttingXY(op).Where(p => Math.Abs(p.Y - 30) < 0.5).ToList();
        Assert.Contains(linePoints, p => p.X < 30);
        Assert.Contains(linePoints, p => p.X > 50);
    }

    [Fact]
    public void Ilha_colada_na_parede_ou_ilhas_juntas_dao_erro_claro()
    {
        Contour box = Contour.Rectangle(0, 0, 80, 60);
        OperationResult wall = CamOperations.PocketWithIslands(box, [Contour.Circle(new Point2(8, 30), 6)], Tool, Levels, Params);
        Assert.False(wall.Ok);
        Assert.Contains("parede", wall.Error);

        OperationResult near = CamOperations.PocketWithIslands(box, [Contour.Circle(new Point2(30, 30), 6), Contour.Circle(new Point2(44, 30), 6)], Tool, Levels, Params);
        Assert.False(near.Ok);
        Assert.Contains("próximas", near.Error);
    }

    [Fact]
    public void Acabamento_contorna_parede_e_ilha()
    {
        Contour box = Contour.Rectangle(0, 0, 80, 60);
        var island = Contour.Circle(new Point2(40, 30), 10);
        CamOperation plain = CamOperations.PocketWithIslands(box, [island], Tool, Levels, Params with { FinishAllowance = 0 }).Operation!;
        CamOperation finish = CamOperations.PocketWithIslands(box, [island], Tool, Levels, Params with { FinishAllowance = 0.3 }).Operation!;
        Assert.True(finish.Moves.Count > plain.Moves.Count);
    }

    [Fact]
    public void Job_com_ilhas_gera_programa_e_salva_e_abre()
    {
        var job = new CamJob { Name = "ILHA" };
        job.Contours.Add(Contour.Rectangle(0, 0, 80, 60));
        job.Contours.Add(Contour.Circle(new Point2(40, 30), 10));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Pocket, Tool, Levels) { ContourIndex = 0, PocketParams = Params, Islands = true });
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));

        JobLoadResult loaded = CamJobJson.FromJson(CamJobJson.ToJson(job));
        Assert.True(loaded.Ok, loaded.Error);
        Assert.True(loaded.Job!.Operations[0].Islands);
        Assert.Equal(build.Program, CamJobBuilder.Build(loaded.Job, null).Program);

        // Sem a opção, o bolsão ignora a ilha (como antes)
        job.Operations[0] = job.Operations[0] with { Islands = false };
        Assert.NotEqual(build.Program, CamJobBuilder.Build(job, null).Program);
    }
}
