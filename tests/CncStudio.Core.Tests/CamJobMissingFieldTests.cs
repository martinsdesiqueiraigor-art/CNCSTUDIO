using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>CAM-12: campo numérico vazio ou inválido deixa a operação Incompleta, bloqueia gerar e salvar, e o número antigo não vale.</summary>
public class CamJobMissingFieldTests
{
    private static readonly string[] Common = ["Ferramenta (T)", "Diâmetro", "Rotação", "Avanço de descida"];

    private static readonly string[] Levels = ["Z seguro (rápido)", "Z de aproximação", "Z topo da peça", "Z fundo", "Passe em Z"];

    // campos obrigatórios por operação, com os mesmos nomes da tela (JobView.Props)
    public static TheoryData<JobOpKind, ContourSide, string> Fields()
    {
        var data = new TheoryData<JobOpKind, ContourSide, string>();
        void Add(JobOpKind k, ContourSide side, IEnumerable<string> fields)
        {
            foreach (string f in fields)
            {
                data.Add(k, side, f);
            }
        }

        string[] plain = [.. Common, "Avanço no plano", .. Levels];
        Add(JobOpKind.Facing, ContourSide.Outside, [.. plain, "Passo lateral"]);
        foreach (ContourSide side in new[] { ContourSide.Outside, ContourSide.Inside, ContourSide.OnLine })
        {
            Add(JobOpKind.Contour, side, [.. plain, "Sobremetal", "Raio de entrada"]);
        }

        Add(JobOpKind.Pocket, ContourSide.Outside, [.. plain, "Passo lateral", "Sobremetal de acabamento", "Raio de entrada"]);
        Add(JobOpKind.Drilling, ContourSide.Outside, plain);
        Add(JobOpKind.WrapText, ContourSide.Outside, [.. Common, "Avanço na superfície", "Altura das letras", "Diâmetro da peça", "Início ao longo do eixo",
            "Ângulo inicial", "Profundidade", "Passe em profundidade", "Folga acima da peça", "Avanço de mergulho"]);
        return data;
    }

    private static CamJob Job(CamOperationSpec op)
    {
        var job = new CamJob { Stock = new JobStock(-5, -5, 70, 50, 20) };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Contours.Add(Contour.Circle(new Point2(30, 20), 12));
        job.Operations.Add(op);
        return job;
    }

    private static CamOperationSpec Op(JobOpKind kind, ContourSide side = ContourSide.Outside) =>
        CamJobInfo.NewOperation(kind, side, kind == JobOpKind.Drilling ? -1 : 0);

    private static MachineProfile? Machine(JobOpKind kind) => kind == JobOpKind.WrapText ? MachineProfile.FourAxisTableA() : null;

    [Theory]
    [MemberData(nameof(Fields))]
    public void Campo_vazio_deixa_Incompleta_bloqueia_gerar_e_salvar_e_corrigir_volta_ao_mesmo_programa(JobOpKind kind, ContourSide side, string field)
    {
        CamJob job = Job(Op(kind, side));
        JobBuild before = CamJobBuilder.Build(job, Machine(kind));
        Assert.True(before.Ok, string.Join("; ", before.Errors));

        // apagar o campo: o valor guardado não muda, mas não vale mais
        job.Operations[0] = CamJobInfo.SetMissing(job.Operations[0], field, true);
        JobBuild build = CamJobBuilder.Build(job, Machine(kind));
        Assert.False(build.Ok);
        Assert.Equal("", build.Program);
        Assert.Equal("Falta " + field, build.Items[0].Error);
        Assert.Equal(("bad", "erro: Falta " + field), CamJobInfo.OpState(build.Items[0], job.Operations[0]));
        IReadOnlyList<string> pending = CamJobInfo.Pending(job, build);
        Assert.Contains($"Operação 1: Falta {field}", pending);
        Assert.Contains(pending, CamJobInfo.Blocks);
        string blocker = Assert.Single(CamJobInfo.SaveBlockers(job));
        Assert.EndsWith("falta " + field, blocker);
        Assert.StartsWith("Operação 1 (", blocker);

        // digitar um valor válido tira o estado (aqui o mesmo número): volta o mesmo programa
        job.Operations[0] = CamJobInfo.SetMissing(job.Operations[0], field, false);
        JobBuild after = CamJobBuilder.Build(job, Machine(kind));
        Assert.True(after.Ok);
        Assert.Equal(before.Program, after.Program);
        Assert.Empty(CamJobInfo.SaveBlockers(job));
    }

    [Fact]
    public void Dois_campos_vazios_aparecem_juntos_no_motivo()
    {
        CamOperationSpec s = CamJobInfo.SetMissing(CamJobInfo.SetMissing(Op(JobOpKind.Pocket), "Z fundo", true), "Diâmetro", true);
        Assert.Equal("Falta Z fundo e Diâmetro", CamJobInfo.MissingError(s));
        s = CamJobInfo.SetMissing(s, "Passe em Z", true);
        Assert.Equal("Falta Z fundo, Diâmetro e Passe em Z", CamJobInfo.MissingError(s));
    }

    [Fact]
    public void Marcar_duas_vezes_nao_duplica_e_desmarcar_o_que_nao_esta_nao_muda()
    {
        CamOperationSpec s = Op(JobOpKind.Pocket);
        Assert.Same(s, CamJobInfo.SetMissing(s, "Z fundo", false));
        CamOperationSpec m = CamJobInfo.SetMissing(s, "Z fundo", true);
        Assert.Same(m, CamJobInfo.SetMissing(m, "Z fundo", true));
        Assert.Single(m.MissingFields);
    }

    [Theory]
    [InlineData(DrillCycleKind.Drill, false, false)]
    [InlineData(DrillCycleKind.Dwell, false, true)]
    [InlineData(DrillCycleKind.Peck, true, false)]
    [InlineData(DrillCycleKind.ChipBreak, true, false)]
    public void Bicada_e_pausa_so_contam_se_o_ciclo_usa(DrillCycleKind cycle, bool peckCounts, bool dwellCounts)
    {
        CamOperationSpec s = Op(JobOpKind.Drilling) with { Drill = new DrillSpec(cycle, 2, 1) };
        Assert.Equal(peckCounts, CamJobInfo.MissingError(CamJobInfo.SetMissing(s, CamJobInfo.PeckField, true)) is not null);
        Assert.Equal(dwellCounts, CamJobInfo.MissingError(CamJobInfo.SetMissing(s, CamJobInfo.DwellField, true)) is not null);

        // num ciclo que não usa, o campo vazio não impede gerar nem salvar
        CamJob job = Job(CamJobInfo.SetMissing(CamJobInfo.SetMissing(s, CamJobInfo.PeckField, !peckCounts), CamJobInfo.DwellField, !dwellCounts));
        Assert.True(CamJobBuilder.Build(job, null).Ok);
        Assert.Empty(CamJobInfo.SaveBlockers(job));
    }

    [Fact]
    public void Campo_da_face_so_conta_com_a_face_ligada_e_no_modo_atual()
    {
        CamOperationSpec s = CamJobInfo.SetMissing(Op(JobOpKind.Pocket), "Ângulo da face", true);
        Assert.Null(CamJobInfo.MissingError(s)); // sem face: campo não está na tela
        s = s with { Face = new RotaryFaceSpec("A", 90, 25) };
        Assert.Equal("Falta Ângulo da face", CamJobInfo.MissingError(s));
        Assert.Equal(["Ângulo da face", "Do centro do eixo até a face"], CamJobInfo.FaceFields(s.Face));

        // 3+2: os campos da face são outros
        s = s with { Face = new RotaryFaceSpec("A", 30, 25) { Axis2 = "C" } };
        Assert.Null(CamJobInfo.MissingError(s));
        Assert.Equal(["Ângulo do basculante (A)", "Ângulo do giratório (C)", "Do centro do eixo A até a face"], CamJobInfo.FaceFields(s.Face));
        Assert.Equal("Falta Ângulo do giratório (C)", CamJobInfo.MissingError(CamJobInfo.SetMissing(s, "Ângulo do giratório (C)", true)));
    }

    [Fact]
    public void Operacao_desligada_com_campo_vazio_gera_mas_nao_salva()
    {
        CamJob job = Job(Op(JobOpKind.Contour));
        job.Operations.Add(CamJobInfo.SetMissing(Op(JobOpKind.Pocket), "Z fundo", true) with { Enabled = false });
        Assert.True(CamJobBuilder.Build(job, null).Ok);
        Assert.Equal(["Operação 2 (Bolsão): falta Z fundo"], CamJobInfo.SaveBlockers(job));
    }

    [Fact]
    public void Titulo_na_arvore_nao_mostra_T_nem_diametro_antigos()
    {
        CamOperationSpec s = Op(JobOpKind.Pocket);
        Assert.Contains("T1 Ø10", CamJobInfo.OpTitle(0, s));
        s = CamJobInfo.SetMissing(CamJobInfo.SetMissing(s, CamJobInfo.ToolNumberField, true), CamJobInfo.DiameterField, true);
        Assert.Contains("T? Ø?", CamJobInfo.OpTitle(0, s));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    [InlineData(3.5, false)]
    [InlineData(double.NaN, false)]
    public void T_so_aceita_inteiro_de_1_para_cima(double v, bool ok) => Assert.Equal(ok, CamJobInfo.ToolNumberError(v) is null);

    [Theory]
    [InlineData("Diâmetro")]
    [InlineData("Passe em Z")]
    [InlineData("Rotação")]
    public void Valor_negativo_onde_nao_faz_sentido_deixa_Incompleta(string field)
    {
        CamOperationSpec s = Op(JobOpKind.Pocket);
        s = field switch
        {
            "Diâmetro" => s with { Tool = s.Tool with { Diameter = -10 } },
            "Passe em Z" => s with { Levels = s.Levels with { StepDown = -2 } },
            _ => s with { Tool = s.Tool with { SpindleRpm = -3000 } },
        };
        JobBuild build = CamJobBuilder.Build(Job(s), null);
        Assert.False(build.Ok);
        Assert.Equal("bad", CamJobInfo.OpState(build.Items[0], s).Kind);
    }

    [Fact]
    public void Campos_vazios_nao_vao_para_o_arquivo_e_arquivo_antigo_abre_sem_eles()
    {
        CamJob job = Job(Op(JobOpKind.Pocket));
        string clean = CamJobJson.ToJson(job);
        job.Operations[0] = CamJobInfo.SetMissing(job.Operations[0], "Z fundo", true);

        // formato igual: a marca não é gravada (o Salvar fica bloqueado na tela)
        Assert.Equal(clean, CamJobJson.ToJson(job));
        JobLoadResult loaded = CamJobJson.FromJson(clean);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Empty(loaded.Job!.Operations[0].MissingFields);
        Assert.True(CamJobBuilder.Build(loaded.Job, null).Ok);
    }

    // ---------------------------------------------------------------- ajustes do teste no .exe (CAM-12-ajuste)

    [Fact]
    public void No_Ferramentas_mostra_interrogacao_no_lugar_do_valor_antigo()
    {
        CamJob job = Job(Op(JobOpKind.Pocket));
        Assert.Equal(["T1 · Ø10 · 3000 rpm"], CamJobInfo.ToolLines(job));
        job.Operations[0] = CamJobInfo.SetMissing(job.Operations[0], CamJobInfo.DiameterField, true);
        Assert.Equal(["T1 · Ø? · 3000 rpm"], CamJobInfo.ToolLines(job));
        job.Operations[0] = CamJobInfo.SetMissing(job.Operations[0], CamJobInfo.SpindleField, true);
        job.Operations[0] = CamJobInfo.SetInvalid(job.Operations[0], CamJobInfo.ToolNumberField, true);
        Assert.Equal(["T? · Ø? · ? rpm"], CamJobInfo.ToolLines(job));

        // a outra operação com a mesma ferramenta, completa, continua aparecendo com os números dela
        job.Operations.Add(Op(JobOpKind.Contour));
        Assert.Equal(["T1 · Ø10 · 3000 rpm", "T? · Ø? · ? rpm"], CamJobInfo.ToolLines(job));
    }

    [Fact]
    public void T_invalido_tem_motivo_proprio_e_vazio_continua_Falta()
    {
        CamOperationSpec s = Op(JobOpKind.Pocket);
        Assert.Equal("T inválido: use inteiro ≥ 1", CamJobInfo.ToolNumberError(3.5));

        CamOperationSpec bad = CamJobInfo.SetFieldState(s, CamJobInfo.ToolNumberField, false, true);
        Assert.Equal("T inválido: use inteiro ≥ 1", CamJobInfo.MissingError(bad));
        Assert.Contains("T? Ø10", CamJobInfo.OpTitle(0, bad));
        CamJob job = Job(bad);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.False(build.Ok);
        Assert.Equal("T inválido: use inteiro ≥ 1", build.Items[0].Error);
        Assert.Equal(["Operação 1 (Bolsão): T inválido: use inteiro ≥ 1"], CamJobInfo.SaveBlockers(job));

        // vazio x inválido: um exclui o outro
        CamOperationSpec empty = CamJobInfo.SetFieldState(bad, CamJobInfo.ToolNumberField, true, false);
        Assert.Equal("Falta Ferramenta (T)", CamJobInfo.MissingError(empty));
        Assert.Empty(empty.InvalidFields);
        CamOperationSpec ok = CamJobInfo.SetFieldState(empty, CamJobInfo.ToolNumberField, false, false);
        Assert.Null(CamJobInfo.MissingError(ok));
        Assert.True(CamJobBuilder.Build(Job(ok), null).Ok);

        // junto com campo vazio
        CamOperationSpec both = CamJobInfo.SetMissing(bad, "Z fundo", true);
        Assert.Equal("Falta Z fundo; T inválido: use inteiro ≥ 1", CamJobInfo.MissingError(both));
    }

    [Theory]
    [InlineData(DrillCycleKind.Drill, false, false)]
    [InlineData(DrillCycleKind.Dwell, false, true)]
    [InlineData(DrillCycleKind.Peck, true, false)]
    [InlineData(DrillCycleKind.ChipBreak, true, false)]
    public void Bicada_e_pausa_so_contam_para_marcar_quando_o_ciclo_usa(DrillCycleKind cycle, bool peck, bool dwell)
    {
        CamOperationSpec s = CamJobInfo.SetMissing(CamJobInfo.SetMissing(Op(JobOpKind.Drilling) with { Drill = new DrillSpec(cycle, 2, 1) },
            CamJobInfo.PeckField, true), CamJobInfo.DwellField, true);
        Assert.Equal(peck, CamJobInfo.Counts(s, CamJobInfo.PeckField));
        Assert.Equal(dwell, CamJobInfo.Counts(s, CamJobInfo.DwellField));
        Assert.True(CamJobInfo.Counts(s, "Z fundo"));

        // G81: os dois vazios e a operação Pronta (sem motivo)
        if (!peck && !dwell)
        {
            JobBuild build = CamJobBuilder.Build(Job(s), null);
            Assert.True(build.Ok);
            Assert.Equal("ok", CamJobInfo.OpState(build.Items[0], s).Kind);
            Assert.Null(CamJobInfo.MissingError(s));
        }
    }
}
