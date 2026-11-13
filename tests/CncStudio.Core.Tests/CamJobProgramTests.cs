using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>UX-04g PR A: Programa nº e G5x digitados, sem padrão escondido.</summary>
public class CamJobProgramTests
{
    private static readonly CamLevels Levels = new(50, 2, 0, -3, 3);

    // trabalho pronto para gerar, sem número nem G5x (como nasce na tela); o zero da peça (PR B) fica "como antes" para isolar o PR A
    private static CamJob Empty(CamDialect dialect = CamDialect.Fanuc)
    {
        var job = new CamJob { Name = "NOVO", Dialect = dialect, Stock = new JobStock(-5, -5, 70, 50, 20), Fixture = new JobFixture("morsa"), Zero = JobZero.Legacy };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Facing, new CamTool(1, 20, 3000, 600, 200, "FACE"), Levels) { ContourIndex = 0 });
        return job;
    }

    private static string RefPath(string name) => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia", name);

    [Fact]
    public void Trabalho_novo_nasce_sem_numero_e_sem_G5x()
    {
        var job = new CamJob();
        Assert.Null(job.ProgramNumber);
        Assert.Null(job.WorkOffset);
    }

    [Fact]
    public void Vazio_nao_gera_com_motivo_mas_salva_em_v3_e_reabre_vazio()
    {
        CamJob job = Empty();
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.False(build.Ok);
        Assert.Equal("", build.Program);
        Assert.Contains(CamJobProgram.ProgramNumberMissing, build.Errors);
        Assert.Contains(CamJobProgram.WorkOffsetMissing, build.Errors);
        // as operações continuam geradas (a árvore mostra o estado delas)
        Assert.All(build.Items, i => Assert.True(i.Ok));

        IReadOnlyList<string> pending = CamJobInfo.Pending(job, build);
        Assert.Contains(CamJobProgram.ProgramNumberMissing, pending);
        Assert.Contains(CamJobProgram.WorkOffsetMissing, pending);
        Assert.True(CamJobInfo.Blocks(CamJobProgram.ProgramNumberMissing));
        Assert.True(CamJobInfo.Blocks(CamJobProgram.WorkOffsetMissing));
        Assert.Empty(CamJobInfo.SaveBlockers(job));

        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 3", json);
        Assert.DoesNotContain("programNumber", json);
        Assert.DoesNotContain("workOffset", json);
        JobLoadResult again = CamJobJson.FromJson(json);
        Assert.True(again.Ok, again.Error);
        Assert.Null(again.Job!.ProgramNumber);
        Assert.Null(again.Job.WorkOffset);
        Assert.Equal(json, CamJobJson.ToJson(again.Job));
    }

    [Fact]
    public void So_um_vazio_tambem_vai_para_v3_e_o_outro_fica_no_arquivo()
    {
        CamJob job = Empty();
        job.WorkOffset = 56;
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 3", json);
        Assert.Contains("\"workOffset\": 56", json);
        CamJob back = CamJobJson.FromJson(json).Job!;
        Assert.Null(back.ProgramNumber);
        Assert.Equal(56, back.WorkOffset);
        Assert.Equal([CamJobProgram.ProgramNumberMissing], CamJobBuilder.Build(back, null).Errors);
    }

    [Fact]
    public void Preenchido_gera_com_o_digitado_e_nao_sobe_de_versao()
    {
        CamJob job = Empty();
        job.ProgramNumber = 4321;
        job.WorkOffset = 57;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("O4321 (NOVO)", build.Program);
        Assert.Contains("\nG57\n", build.Program);
        Assert.Contains("\"version\": 1", CamJobJson.ToJson(job));

        job.PartHeight = 10; // algo do UX-04f: versão 2, não 3
        Assert.Contains("\"version\": 2", CamJobJson.ToJson(job));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10000)]
    public void Numero_fora_da_faixa_vindo_do_arquivo_gera_com_aviso(int number)
    {
        CamJob job = Empty();
        job.ProgramNumber = number;
        job.WorkOffset = 54;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains(build.Warnings, w => w.Contains("veio do arquivo") && w.Contains("1 a 9999"));
        Assert.Empty(CamJobProgram.Problems(job));
        Assert.DoesNotContain(CamJobInfo.Pending(job, build), CamJobProgram.IsProgramProblem);
    }

    [Theory]
    [InlineData("", null, false)]
    [InlineData("1", 1, false)]
    [InlineData(" 9999 ", 9999, false)]
    [InlineData("0", null, true)]
    [InlineData("-1", null, true)]
    [InlineData("10000", null, true)]
    [InlineData("12.5", null, true)]
    [InlineData("O100", null, true)]
    [InlineData("abc", null, true)]
    public void Campo_do_numero_le_so_inteiro_de_1_a_9999(string typed, int? number, bool error)
    {
        (int? n, string? why) = CamJobProgram.ParseProgramNumber(typed);
        Assert.Equal(number, n);
        Assert.Equal(error, why is not null);
    }

    [Fact]
    public void Siemens_nao_exige_numero_e_gera_igual()
    {
        CamJob job = Empty(CamDialect.Siemens);
        job.WorkOffset = 54;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.DoesNotContain(CamJobProgram.ProgramNumberMissing, CamJobInfo.Pending(job, build));
        Assert.StartsWith("; NOVO\n", build.Program);
        Assert.DoesNotContain("O0", build.Program);
        Assert.Equal("G54", CamJobProgram.StatusText(job));

        // o mesmo trabalho com um número qualquer gera o mesmo texto (o Siemens não escreve o número)
        job.ProgramNumber = 1001;
        Assert.Equal(build.Program, CamJobBuilder.Build(job, null).Program);
    }

    [Fact]
    public void Lista_do_G5x_depende_do_controle()
    {
        Assert.Equal([54, 55, 56, 57, 58, 59], CamJobProgram.WorkOffsets(CamDialect.Fanuc));
        Assert.Equal([54, 55, 56, 57], CamJobProgram.WorkOffsets(CamDialect.Siemens));
        Assert.NotNull(CamJobProgram.WorkOffsetError(58, CamDialect.Siemens));
        Assert.Null(CamJobProgram.WorkOffsetError(58, CamDialect.Fanuc));
        Assert.NotNull(CamJobProgram.WorkOffsetError(60, CamDialect.Fanuc));

        // G58 no Siemens só chega pelo arquivo: gera, com aviso
        CamJob job = Empty(CamDialect.Siemens);
        job.WorkOffset = 58;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("\nG58\n", build.Program);
        Assert.Contains(build.Warnings, w => w.StartsWith("G58 veio do arquivo", StringComparison.Ordinal));
    }

    [Fact]
    public void Arquivo_Siemens_com_G58_gera_o_mesmo_texto_de_antes()
    {
        string json = File.ReadAllText(RefPath("fresa-bloco-siemens.casillas-job")).Replace("\r\n", "\n");
        string expected = File.ReadAllText(RefPath("fresa-bloco-siemens.nc")).Replace("\r\n", "\n").Replace("\nG54\n", "\nG58\n");
        CamJob job = CamJobJson.FromJson(json.Replace("\"workOffset\": 54", "\"workOffset\": 58")).Job!;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Equal(expected.TrimEnd(), build.Program.TrimEnd());
        Assert.Single(build.Warnings, w => w.Contains("veio do arquivo"));
    }

    [Theory]
    [InlineData(0, "O0000 ")]
    [InlineData(10000, "O10000 ")]
    public void Arquivo_com_numero_fora_da_faixa_gera_o_mesmo_texto_de_antes(int number, string line)
    {
        string json = File.ReadAllText(RefPath("fresa-sem-bruto.casillas-job")).Replace("\r\n", "\n");
        string expected = File.ReadAllText(RefPath("fresa-sem-bruto.nc")).Replace("\r\n", "\n").Replace("O1001 ", line);
        JobLoadResult loaded = CamJobJson.FromJson(json.Replace("\"programNumber\": 1001", "\"programNumber\": " + number));
        Assert.True(loaded.Ok, loaded.Error);
        JobBuild build = CamJobBuilder.Build(loaded.Job!, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Equal(expected.TrimEnd(), build.Program.TrimEnd());
        Assert.Contains(build.Warnings, w => w.Contains($"Programa nº {number} veio do arquivo"));
    }

    [Fact]
    public void Rodape_mostra_o_digitado_ou_falta_e_nunca_1001()
    {
        CamJob job = Empty();
        Assert.Equal("Programa nº: falta · G5x: falta", CamJobProgram.StatusText(job));
        Assert.Equal("Origem G5x (falta): X0 Y0 do desenho, topo Z0", CamJobInfo.OriginTitle(job));
        job.ProgramNumber = 7;
        job.WorkOffset = 55;
        Assert.Equal("Programa nº 7 · G55", CamJobProgram.StatusText(job));
    }

    [Fact]
    public void Leitor_ate_v2_exige_os_dois_campos_e_v3_aceita_sem_eles()
    {
        CamJob job = Empty();
        job.ProgramNumber = 1001;
        job.WorkOffset = 54;
        string v1 = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 1", v1);
        string semNumero = v1.Replace("\"programNumber\": 1001,", "");
        Assert.False(CamJobJson.FromJson(semNumero).Ok);
        JobLoadResult v3 = CamJobJson.FromJson(semNumero.Replace("\"version\": 1", "\"version\": 3"));
        Assert.True(v3.Ok, v3.Error);
        Assert.Null(v3.Job!.ProgramNumber);
        Assert.Equal(54, v3.Job.WorkOffset);
        Assert.False(CamJobJson.FromJson(v1.Replace("\"version\": 1", "\"version\": 4")).Ok);
    }

    [Theory]
    [InlineData("fresa-sem-bruto", 1001, 54)]
    [InlineData("fresa-fixacao", 2024, 55)]
    [InlineData("fresa-bloco-siemens", 1001, 54)]
    public void Arquivo_antigo_traz_numero_e_G5x_e_salva_na_mesma_versao(string name, int number, int offset)
    {
        string json = File.ReadAllText(RefPath(name + ".casillas-job")).Replace("\r\n", "\n");
        CamJob job = CamJobJson.FromJson(json).Job!;
        Assert.Equal(number, job.ProgramNumber);
        Assert.Equal(offset, job.WorkOffset);
        Assert.Empty(CamJobProgram.Problems(job));
        Assert.Contains("\"version\": 1", CamJobJson.ToJson(job));
    }
}
