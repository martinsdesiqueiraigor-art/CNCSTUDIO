using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>Faixa do Trabalho torno (UX-04e) e campos vazios no torno (regra do CAM-12).</summary>
public class LathePaletteMissingTests
{
    private static LatheProfile Profile()
    {
        Point2[] pts = [new(0, 8), new(-2, 10), new(-25, 10), new(-25, 14), new(-35, 14), new(-45, 14)];
        return LatheProfile.From(Enumerable.Range(1, pts.Length - 1).Select(i => Segment2.Line(pts[i - 1], pts[i])).ToList(), out _)!;
    }

    private static LatheJob Sample()
    {
        var job = new LatheJob { Name = "EIXO TESTE", Profile = Profile() };
        foreach ((string _, LatheOpKind kind, bool isInternal) in LatheJobInfo.NewKinds.Where(k => !k.Internal))
        {
            job.Operations.Add(LatheJobInfo.NewOperation(kind, isInternal, new LatheTool((int)kind + 1, "Ferr", 200, 3000, 0)));
        }

        return job;
    }

    private static CamPaletteItem Item(string id) => CamPalette.LatheGroups.SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void Faixa_do_torno_tem_cada_operacao_do_Core_uma_vez()
    {
        var ops = CamPalette.LatheVisibleGroups.SelectMany(g => g.Items).Where(i => i.LatheKind is not null).ToList();
        Assert.Equal(LatheJobInfo.NewKinds.Count, ops.Count);
        foreach ((string _, LatheOpKind kind, bool isInternal) in LatheJobInfo.NewKinds)
        {
            Assert.Single(ops, i => i.LatheKind == kind && i.Internal == isInternal);
        }

        // a faixa do torno não cria operação de fresa
        Assert.All(CamPalette.LatheGroups.SelectMany(g => g.Items), i => Assert.Null(i.Kind));
    }

    [Fact]
    public void Grupos_do_torno_na_ordem_e_em_breve_apagados()
    {
        Assert.Equal(["Arquivo", "Fixação", "Torneamento", "Canal e corte", "Rosca"], CamPalette.LatheVisibleGroups.Select(g => g.Title));
        foreach (string id in new[] { "nova-fixacao", "zero", "canal-face", "sangrar" })
        {
            CamPaletteItem item = Item(id);
            Assert.True(item.Soon);
            Assert.Equal(CamPalette.SoonMessage, CamPalette.LatheUnavailable(item, new LatheJob()));
            Assert.Null(CamPalette.NewLatheOperation(item));
        }

        Assert.Equal(["Canal externo", "Canal interno", "Canal de face", "Sangrar"], CamPalette.LatheGroups.Single(g => g.Id == "canal").Items.Select(i => i.Name));
        Assert.Equal("abrir-dxf", Item("abrir-dxf").Action);
        Assert.Null(CamPalette.LatheUnavailable(Item("salvar-trabalho"), new LatheJob()));
    }

    [Fact]
    public void Torneamento_pede_o_perfil_e_os_outros_nao()
    {
        var empty = new LatheJob();
        Assert.NotNull(CamPalette.LatheUnavailable(Item("torneamento-externo"), empty));
        Assert.NotNull(CamPalette.LatheUnavailable(Item("torneamento-interno"), empty));
        Assert.Null(CamPalette.LatheUnavailable(Item("faceamento"), empty));
        Assert.Null(CamPalette.LatheUnavailable(Item("furar"), empty));
        Assert.Null(CamPalette.LatheUnavailable(Item("rosca-interna"), empty));
        var withProfile = new LatheJob { Profile = Profile() };
        Assert.Null(CamPalette.LatheUnavailable(Item("torneamento-externo"), withProfile));
    }

    [Fact]
    public void Botao_cria_a_mesma_operacao_da_lista_antiga_e_o_icone_volta()
    {
        foreach (CamPaletteItem item in CamPalette.LatheGroups.SelectMany(g => g.Items).Where(i => i.LatheKind is not null))
        {
            LatheOperationSpec made = CamPalette.NewLatheOperation(item)!;
            LatheOperationSpec old = LatheJobInfo.NewOperation(item.LatheKind!.Value, item.Internal);
            Assert.Equal(LatheJobJson.ToJson(Wrap(old)), LatheJobJson.ToJson(Wrap(made)));
            Assert.Same(item, CamPalette.LatheItemFor(made));
        }

        static LatheJob Wrap(LatheOperationSpec s)
        {
            var j = new LatheJob();
            j.Operations.Add(s);
            return j;
        }
    }

    [Fact]
    public void Campo_vazio_deixa_a_operacao_incompleta_e_o_valor_volta_a_gerar_igual()
    {
        LatheJob job = Sample();
        string before = LatheJobBuilder.Build(job).Program;
        Assert.NotEqual("", before);
        int thread = job.Operations.FindIndex(o => o.Kind == LatheOpKind.Threading);
        LatheOperationSpec original = job.Operations[thread];

        job.Operations[thread] = LatheJobInfo.SetMissing(original, "Passo", true);
        LatheBuild bad = LatheJobBuilder.Build(job);
        Assert.False(bad.Ok);
        Assert.Equal("", bad.Program);
        Assert.Equal("Falta Passo", bad.Items[thread].Error);
        Assert.Contains($"Operação {thread + 1}: Falta Passo", LatheJobInfo.Pending(job, bad));
        Assert.Equal([$"Operação {thread + 1} (Rosca externa): falta Passo"], LatheJobInfo.SaveBlockers(job));

        job.Operations[thread] = LatheJobInfo.SetMissing(job.Operations[thread], "Z do fim", true);
        Assert.Equal("Falta Passo e Z do fim", LatheJobBuilder.Build(job).Items[thread].Error);

        // desligada também bloqueia salvar (o arquivo guardaria o número antigo)
        job.Operations[thread] = job.Operations[thread] with { Enabled = false };
        Assert.Single(LatheJobInfo.SaveBlockers(job));

        job.Operations[thread] = LatheJobInfo.SetMissing(LatheJobInfo.SetMissing(job.Operations[thread] with { Enabled = true }, "Passo", false), "Z do fim", false);
        Assert.Empty(job.Operations[thread].MissingFields);
        Assert.Equal(before, LatheJobBuilder.Build(job).Program);
        Assert.Empty(LatheJobInfo.SaveBlockers(job));
    }

    [Fact]
    public void Campos_vazios_nao_vao_para_o_arquivo()
    {
        LatheJob job = Sample();
        string clean = LatheJobJson.ToJson(job);
        job.Operations[0] = LatheJobInfo.SetMissing(job.Operations[0], "Avanço", true);
        Assert.Equal(clean, LatheJobJson.ToJson(job));
        Assert.Empty(LatheJobJson.FromJson(clean).Job!.Operations[0].MissingFields);
    }

    [Fact]
    public void Bruto_do_torno_e_so_leitura_dos_diametros_das_operacoes()
    {
        var job = new LatheJob { Profile = Profile() };
        Assert.Equal("Bruto (barra): sem operação com Ø do bruto", LatheJobInfo.StockTitle(job));
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));   // Ø 40
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Turning, false));  // Ø 40
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Turning, true));   // interno: pré-furo, não entra
        Assert.Equal("Bruto (barra): Ø 40 (op. 1, 2)", LatheJobInfo.StockTitle(job));
        job.Operations[1] = LatheJobInfo.SetMissing(job.Operations[1], LatheJobInfo.StockField, true);
        Assert.Equal("Bruto (barra): Ø 40 (op. 1) · Ø ? (op. 2)", LatheJobInfo.StockTitle(job));
    }

    [Fact]
    public void Ferramenta_vazia_aparece_com_interrogacao()
    {
        var job = new LatheJob();
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false, new LatheTool(2, "", 200, 3000, 0)));
        Assert.Equal(["T2 · G96 200 m/min · máx. 3000 rpm"], LatheJobInfo.ToolLines(job));
        job.Operations[0] = LatheJobInfo.SetMissing(job.Operations[0], LatheJobInfo.ToolNumberField, true);
        job.Operations[0] = LatheJobInfo.SetMissing(job.Operations[0], LatheJobInfo.MaxRpmField, true);
        Assert.Equal(["T? · G96 200 m/min · máx. ? rpm"], LatheJobInfo.ToolLines(job));
    }
}
