using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T3: sangramento (corte) em G0/G1, igual nos dois controles. Conteúdo técnico em rascunho, nunca rodado em máquina.</summary>
public class LathePartingTests
{
    private static readonly LatheTool Tool = new(6, "Bedame 3", 100, 1800, 0);

    // corta um Ø40 até o centro em Z -30; avanço reduzido do Ø 8 para baixo
    private static PartingParams P() => new(-30, 40, 0, 3, 0.08, 0.03, 8, 5, 0.5, 2);

    private static LatheOperationSpec Spec(PartingParams? p = null) => new(LatheOpKind.Parting, Tool) { Parting = p ?? P() };

    private static LatheJob Job(CamDialect dialect = CamDialect.Fanuc, PartingParams? p = null)
    {
        var job = new LatheJob { Name = "SANGRAR", Dialect = dialect };
        job.Operations.Add(Spec(p));
        return job;
    }

    [Fact]
    public void Corta_no_Z_ate_o_diametro_final_com_avanco_reduzido_no_fim()
    {
        LatheResult r = LatheParting.Build(Tool, 50, 40, P());
        Assert.True(r.Ok, r.Error);
        List<LatheMove> cuts = r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed).ToList();
        Assert.All(cuts, m => Assert.Equal(-30, m.Z));
        Assert.Equal(0, cuts[^1].R, 9);
        // acima do Ø 8 (raio 4) o avanço normal; do Ø 8 para baixo o reduzido, com a troca exatamente no Ø 8
        Assert.All(cuts.Where(m => m.R >= 4 - 1e-9), m => Assert.Equal(0.08, m.Feed));
        Assert.All(cuts.Where(m => m.R < 4 - 1e-9), m => Assert.Equal(0.03, m.Feed));
        Assert.Contains(cuts, m => Math.Abs(m.R - 4) < 1e-9);
        Assert.Equal(0.03, cuts[^1].Feed);
        // mergulhos de 5 mm no raio: 15, 10, 5, (4 na troca) e 0
        Assert.Equal([15, 10, 5, 4, 0], cuts.Select(m => Math.Round(m.R, 6)));
        // sai radialmente antes de ir ao Z seguro
        LatheMove last = r.Operation.Moves[^1], beforeLast = r.Operation.Moves[^2];
        Assert.Equal((50d, 40d), (last.Z, last.R));
        Assert.Equal((-30d, 40d), (beforeLast.Z, beforeLast.R));
    }

    [Fact]
    public void Diametro_final_zero_tem_aviso_fixo_de_peca_solta_e_pino_nao_tem()
    {
        LatheResult center = LatheParting.Build(Tool, 50, 40, P());
        Assert.Equal([LatheParting.Warning, LatheParting.LoosePartWarning], center.Operation!.Warnings);
        Assert.Contains("se solta", LatheParting.LoosePartWarning);

        LatheResult pin = LatheParting.Build(Tool, 50, 40, P() with { FinalDiameter = 4 });
        Assert.True(pin.Ok, pin.Error);
        Assert.Equal([LatheParting.Warning], pin.Operation!.Warnings);
        Assert.Equal(2, pin.Operation.Moves.Where(m => m.Kind == MoveKind.Feed).Min(m => m.R), 9);

        // o aviso chega ao programa gerado (lista de avisos da tela)
        Assert.Contains(LatheParting.LoosePartWarning, LatheJobBuilder.Build(Job()).Warnings);
    }

    [Fact]
    public void Avanco_reduzido_e_limite_de_rotacao_sao_obrigatorios()
    {
        string? E(PartingParams p) => LatheParting.Build(Tool, 50, 40, p).Error;
        Assert.Contains("maiores que zero", E(P() with { ReducedFeed = 0 }));
        Assert.Contains("avanço reduzido precisa ser maior que o Ø final", E(P() with { ReducedFromDiameter = 0 }));
        Assert.Contains("avanço reduzido precisa ser maior que o Ø final", E(P() with { FinalDiameter = 10, ReducedFromDiameter = 10 }));
        Assert.Contains("menor que o Ø de início", E(P() with { FinalDiameter = 40 }));
        Assert.Contains("negativo", E(P() with { FinalDiameter = -1 }));
        Assert.Contains("maiores que zero", E(P() with { Peck = 0 }));
        Assert.Contains("rotação máxima", LatheParting.Build(Tool with { MaxRpm = 0 }, 50, 40, P()).Error);

        LatheOperationSpec blank = LatheJobInfo.NewBlank(LatheOpKind.Parting, false);
        Assert.Contains(LatheJobInfo.ReducedFeedField, blank.MissingFields);
        Assert.Contains(LatheJobInfo.ReducedFromField, blank.MissingFields);
        Assert.Contains(LatheJobInfo.MaxRpmField, blank.MissingFields);
        Assert.Contains(LatheJobInfo.PartingFinalField, blank.MissingFields);
        Assert.Equal("Sangramento", LatheJobInfo.KindName(blank));
    }

    [Fact]
    public void Limite_de_rotacao_acima_do_fuso_nao_gera_o_sangramento()
    {
        LatheJob job = Job();
        Assert.True(LatheJobBuilder.Build(job).Ok);                 // sem perfil de máquina: não há o que conferir
        Assert.True(LatheJobBuilder.Build(job, 1800).Ok);           // no limite
        LatheBuild over = LatheJobBuilder.Build(job, 1500);
        Assert.False(over.Ok);
        Assert.Contains("acima do limite do fuso (1500 rpm)", over.Errors[0]);
        Assert.StartsWith("Operação 1: Rotação máxima (G50)", over.Errors[0]);

        // nas outras operações a rotação acima do fuso continua só aviso
        var facing = new LatheJob();
        facing.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        Assert.True(LatheJobBuilder.Build(facing, 1500).Ok);
    }

    [Fact]
    public void Nada_vem_depois_do_sangramento()
    {
        LatheJob job = Job();
        Assert.Null(LatheParting.FirstAfterParting(job.Operations));
        CamPaletteItem facing = CamPalette.LatheGroups.SelectMany(g => g.Items).Single(i => i.Id == "faceamento");
        Assert.StartsWith(LatheParting.LastOperation, CamPalette.LatheUnavailable(facing, job));

        // arquivo com operação depois do sangramento: abre, mas a operação de depois não gera
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        Assert.Equal(1, LatheParting.FirstAfterParting(job.Operations));
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.False(build.Ok);
        Assert.Equal(["Operação 2: " + LatheParting.LastOperation], build.Errors);
        LatheLoadResult back = LatheJobJson.FromJson(LatheJobJson.ToJson(job));
        Assert.True(back.Ok, back.Error);

        // sangramento desligado não trava as de depois; antes dele tudo gera
        job.Operations[0] = job.Operations[0] with { Enabled = false };
        Assert.True(LatheJobBuilder.Build(job).Ok);
        job.Operations.Reverse();
        job.Operations[1] = job.Operations[1] with { Enabled = true };
        Assert.Null(LatheParting.FirstAfterParting(job.Operations));
        LatheBuild ok = LatheJobBuilder.Build(job);
        Assert.True(ok.Ok, string.Join("; ", ok.Errors));
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Programa_sai_em_G0_G1_e_o_simulador_le(CamDialect dialect)
    {
        LatheBuild build = LatheJobBuilder.Build(Job(dialect));
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        string program = build.Program;
        Assert.Contains("SIMULE ANTES DE RODAR NA MAQUINA", program);
        Assert.Contains(dialect == CamDialect.Fanuc ? "(Sangramento)" : "; Sangramento", program);
        Assert.Contains(dialect == CamDialect.Fanuc ? "G50 S1800" : "LIMS=1800", program);
        Assert.Contains(dialect == CamDialect.Fanuc ? "G1 X0. Z-30. F0.03" : "G1 X0 Z-30 F0.03", program);
        Assert.All(program.Where(c => c > 127), c => Assert.Fail($"caractere fora do ASCII no programa: {c}"));

        var path = MachineExecutor.Execute(GCodeParser.Parse(program, dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings { Machine = MachineKind.Lathe });
        var cuts = path.Moves.Where(m => !m.IsRapid).ToList();
        Assert.All(cuts, m => Assert.Equal(-30, m.End.Z, 3));
        Assert.Equal(0, cuts.Min(m => m.End.X), 3);
    }

    [Fact]
    public void Salvar_e_abrir_e_botao_da_faixa()
    {
        LatheJob job = Job(CamDialect.Siemens, P() with { FinalDiameter = 3 });
        string json = LatheJobJson.ToJson(job);
        Assert.Contains("\"parting\"", json);
        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(job.Operations[0].Parting, back.Job!.Operations[0].Parting);
        Assert.Equal(json, LatheJobJson.ToJson(back.Job));

        CamPaletteItem item = CamPalette.LatheGroups.SelectMany(g => g.Items).Single(i => i.Id == "sangrar");
        Assert.False(item.Soon);
        LatheOperationSpec made = CamPalette.NewLatheOperation(item)!;
        Assert.Equal(LatheOpKind.Parting, made.Kind);
        Assert.Same(item, CamPalette.LatheItemFor(made));
        Assert.Equal(3, LatheJobInfo.WithToolWidth(made, 3).Parting!.ToolWidth);
    }
}
