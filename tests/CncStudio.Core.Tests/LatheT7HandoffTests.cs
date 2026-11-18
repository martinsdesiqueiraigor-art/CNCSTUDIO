using CncStudio.Core.Cam;
using CncStudio.Core.Execution;
using CncStudio.Core.Setup;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T7: pacote .casillas-cam do torno (bruto, fixação, ferramentas), versão 1.</summary>
public class LatheT7HandoffTests
{
    private static LatheJob Eixo()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia", "torno-eixo.casillas-job"));
        LatheLoadResult r = LatheJobJson.FromJson(json);
        Assert.True(r.Ok, r.Error);
        return r.Job!;
    }

    [Fact]
    public void Pacote_do_torno_leva_maquina_numeros_e_amarra_o_programa()
    {
        LatheJob job = Eixo();
        string program = LatheJobBuilder.Build(job).Program;
        CamHandoff p = CamHandoff.CreateLathe(job, program, null);
        Assert.Equal(CamHandoff.Lathe, p.Machine);
        Assert.Equal(CamHandoff.CurrentVersion, p.Version);
        Assert.Equal(job.ProgramNumber, p.ProgramNumber);
        Assert.Equal(job.WorkOffset, p.WorkOffset);
        Assert.True(p.Matches(program));
        Assert.Null(p.Zero);
        Assert.Null(p.Stock); // o trabalho antigo não tem bruto
        Assert.Contains(CamHandoffCode.Notices(p, null, null, program, _ => 0, new Dictionary<int, Geometry.Point3>()), n => n.Kind == HandoffNoticeKind.NoStock);
    }

    [Fact]
    public void Ferramentas_de_torno_vao_sem_diametro_e_nao_mudam_a_tabela()
    {
        LatheJob job = Eixo();
        CamHandoff p = CamHandoff.CreateLathe(job, LatheJobBuilder.Build(job).Program, null);
        Assert.NotEmpty(p.Tools);
        Assert.All(p.Tools, t => Assert.Equal(0, t.Diameter));
        Assert.Equal(job.Operations.Where(o => o.Enabled).Select(o => o.Tool.Number).Distinct().Order(), p.Tools.Select(t => t.Number));
        Assert.Empty(CamHandoffCode.Diameters(p));
    }

    [Theory]
    [InlineData(null, 2.0)]                       // Z0 do perfil: face do bruto em Z 2
    [InlineData(LatheZeroFace.Finished, 2.0)]     // perfil começa em Z 0: nada desloca
    public void Barra_vai_no_referencial_do_programa(LatheZeroFace? face, double top)
    {
        LatheJob job = Eixo();
        job.ZeroFace = face;
        job.Stock = new LatheJobStock(50, 120, 2);
        HandoffStock s = CamHandoff.LatheStockInProgram(job)!;
        Assert.Equal(HandoffStockKind.Round, s.Kind);
        Assert.Equal(50, s.Diameter);
        Assert.Equal(120, s.Height);
        Assert.Equal(top - 120, s.Z, 6);
    }

    [Fact]
    public void Face_oposta_desloca_a_barra_como_o_programa()
    {
        LatheJob job = Eixo();
        job.Stock = new LatheJobStock(50, 120, 2);
        job.ZeroFace = LatheZeroFace.Opposite;
        double shift = LatheZero.Shift(job);
        Assert.NotEqual(0, shift);
        Assert.Equal(2 + shift - 120, CamHandoff.LatheStockInProgram(job)!.Z, 6);
    }

    [Fact]
    public void Bruto_incompleto_nao_vai()
    {
        LatheJob job = Eixo();
        job.Stock = new LatheJobStock(50, null, 2);
        Assert.Null(CamHandoff.LatheStockInProgram(job));
    }

    [Fact]
    public void Code_preenche_o_setup_com_a_barra_do_torno()
    {
        LatheJob job = Eixo();
        job.Stock = new LatheJobStock(50, 120, 2);
        string program = LatheJobBuilder.Build(job).Program;
        CamHandoff p = CamHandoff.CreateLathe(job, program, null);
        var setup = new MachiningSetup();
        Assert.True(CamHandoffCode.FillStock(setup, p, program));
        Simulation.Stock bar = setup.Stock!.ToStock(MachineKind.Lathe)!;
        Assert.Equal(MachineKind.Lathe, bar.Machine);
        Assert.Equal(50, bar.Diameter, 6);
        Assert.Equal(2, bar.MaxZ, 6);
        Assert.Equal(-118, bar.MinZ, 6);
    }

    [Fact]
    public void Fixacao_vai_em_texto_e_pacote_volta_do_arquivo()
    {
        LatheJob job = Eixo();
        job.Stock = new LatheJobStock(50, 120, 2);
        job.Fixation = new LatheFixation(LatheFixationKind.SoftJaws, 40) { Tailstock = true };
        string program = LatheJobBuilder.Build(job).Program;
        CamHandoff p = CamHandoff.CreateLathe(job, program, null, new ToolBank("b", "Torno", ToolBankMachine.Lathe));
        Assert.Equal(new JobFixture("placa", "castanha mole · Ø40 · contraponto"), p.Fixture);
        Assert.Equal("outro", CamHandoff.LatheFixture(new LatheFixation(LatheFixationKind.Center))!.Kind);

        HandoffReadResult back = CamHandoffJson.Parse(CamHandoffJson.ToJson(p));
        Assert.Null(back.Warning);
        CamHandoff q = back.Package!;
        Assert.Equal(CamHandoff.Lathe, q.Machine);
        Assert.Equal(p.Stock, q.Stock);
        Assert.Equal(p.Fixture, q.Fixture);
        Assert.Equal(p.Tools.Count, q.Tools.Count);
        Assert.Equal("Torno", q.BankName);
        Assert.True(q.Matches(program));
    }
}
