using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>UX-04f PR 2: cartão Máquina do Trabalho fresa (escolher, criar e editar perfil). O G-code de 3 eixos não muda.</summary>
public class MachineCardTests
{
    [Fact]
    public void Rotacao_vazia_e_OK_com_fuso_nao_verificado_e_com_rotacao_continua_OK()
    {
        var m = new MachineProfile { Name = "VMC", Controller = CamDialect.Fanuc };
        Assert.Equal(("ok", "OK · fuso não verificado"), CamJobInfo.MillMachineState(m, CamDialect.Fanuc));
        m.MaxSpindleRpm = 8000;
        Assert.Equal(("ok", "OK"), CamJobInfo.MillMachineState(m, CamDialect.Fanuc));
        Assert.Equal(("pending", "sem perfil"), CamJobInfo.MillMachineState(null, CamDialect.Fanuc));
    }

    [Fact]
    public void Controle_diferente_avisa_e_nao_bloqueia_nem_troca_o_do_trabalho()
    {
        var job = new CamJob { Name = "AVISO", Dialect = CamDialect.Fanuc };
        job.Contours.Add(Contour.Rectangle(0, 0, 40, 30));
        job.SetStock(new JobStock(0, 0, 40, 30, 10), null);
        job.Fixture = new JobFixture("vise", "");
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0));
        var siemens = new MachineProfile { Name = "VMC S", Controller = CamDialect.Siemens };

        Assert.Equal(("pending", "aviso: controle diferente do trabalho"), CamJobInfo.MillMachineState(siemens, job.Dialect));
        Assert.Contains("O programa sai em Fanuc", CamJobInfo.MachineControlWarning(siemens, job.Dialect));
        Assert.Null(CamJobInfo.MachineControlWarning(siemens, CamDialect.Siemens));

        JobBuild build = CamJobBuilder.Build(job, siemens);
        Assert.True(build.Ok);
        Assert.DoesNotContain(CamJobInfo.Pending(job, build, siemens.MaxSpindleRpm), CamJobInfo.Blocks);
        Assert.Equal(CamDialect.Fanuc, job.Dialect);
        Assert.Equal(CamJobBuilder.Build(job, null).Program, build.Program);   // 3 eixos: o perfil não muda o programa
    }

    [Fact]
    public void Criar_com_4o_eixo_e_rotacao_vazia_usa_o_modelo_pronto()
    {
        (MachineProfile? p, IReadOnlyList<string> errors) = MachineProfileForm.Build(null, "  Romi D800  ", CamDialect.Fanuc, "4º eixo (A)", "");
        Assert.Empty(errors);
        Assert.Equal("Romi D800", p!.Name);
        Assert.Null(p.MaxSpindleRpm);
        Assert.Equal("4º eixo (A)", CamJobInfo.MachineAxesLabel(p));
        Assert.Equal(MachineProfile.FourAxisTableA().Axes[0].MinAngle, p.Axes[0].MinAngle);
    }

    [Theory]
    [InlineData("", "Fanuc", "3 eixos", "", "Dê um nome")]
    [InlineData("VMC", null, "3 eixos", "", "Escolha o controle")]
    [InlineData("VMC", "Fanuc", null, "", "Escolha os eixos")]
    [InlineData("VMC", "Fanuc", "3 eixos", "rápido", "número inválido")]
    [InlineData("VMC", "Fanuc", "3 eixos", "-10", "maior que zero")]
    public void Nome_controle_e_eixos_sao_obrigatorios_e_a_rotacao_segue_a_validacao_do_perfil(string name, string? controller, string? axes, string rpm, string error)
    {
        CamDialect? c = controller is null ? null : Enum.Parse<CamDialect>(controller);
        (MachineProfile? p, IReadOnlyList<string> errors) = MachineProfileForm.Build(null, name, c, axes, rpm);
        Assert.Null(p);
        Assert.Contains(errors, e => e.Contains(error, StringComparison.Ordinal));
    }

    [Fact]
    public void Editar_perfil_personalizado_nao_perde_nada()
    {
        MachineProfile custom = MachineProfile.FourAxisTableA("4º em B");
        custom.Axes[0].Letter = "B";
        custom.Axes[0].PivotZ = -42.5;
        custom.Axes[0].Reversed = true;
        custom.Mode = RotaryOutputMode.Native;
        Assert.Equal(CamJobInfo.MachineAxesCustom, CamJobInfo.MachineAxesLabel(custom));

        // o formulário não sabe desenhar o personalizado: eixos null = mantém
        (MachineProfile? p, IReadOnlyList<string> errors) = MachineProfileForm.Build(custom, "4º em B", CamDialect.Siemens, null, "6000");
        Assert.Empty(errors);
        Assert.Equal(CamDialect.Siemens, p!.Controller);
        Assert.Equal(6000, p.MaxSpindleRpm);
        Assert.Equal(MachineProfileJson.Serialize(custom).Replace("\"Controller\": \"Fanuc\"", "\"Controller\": \"Siemens\"").Replace("\"MaxSpindleRpm\": null", "\"MaxSpindleRpm\": 6000"),
            MachineProfileJson.Serialize(p));
        Assert.Equal(-42.5, custom.Axes[0].PivotZ);   // o salvo não foi mexido pelo formulário
    }

    [Fact]
    public void Editar_so_a_rotacao_mantem_pivos_do_mesmo_botao_e_trocar_o_botao_usa_o_modelo()
    {
        MachineProfile a = MachineProfile.FourAxisTableA("Mesa A");
        a.Axes[0].PivotZ = -30;
        a.MaxSpindleRpm = 8000;
        MachineProfile same = MachineProfileForm.Build(a, "Mesa A", CamDialect.Fanuc, "4º eixo (A)", "10000").Profile!;
        Assert.Equal(-30, same.Axes[0].PivotZ);
        Assert.Equal(10000, same.MaxSpindleRpm);

        MachineProfile three = MachineProfileForm.Build(a, "Mesa A", CamDialect.Fanuc, "3 eixos", "8000").Profile!;
        Assert.Empty(three.Axes);
    }

    [Fact]
    public void Copia_do_perfil_leva_a_rotacao_maxima()
    {
        var m = new MachineProfile { Name = "VMC", MaxSpindleRpm = 12000 };
        Assert.Equal(12000, m.Clone().MaxSpindleRpm);
    }

    [Fact]
    public void Salvar_cria_edita_escolhe_e_recusa_nome_repetido()
    {
        var all = new List<MachineProfile> { MachineProfile.ThreeAxis("VMC") };

        MachineProfile novo = MachineProfileForm.Build(null, "Romi", CamDialect.Fanuc, "4º eixo (A)", "").Profile!;
        (string active, string? error) = MachineProfileForm.Save(all, null, novo, "VMC");
        Assert.Null(error);
        Assert.Equal("Romi", active);   // o perfil criado passa a ser o ativo
        Assert.Equal(["VMC", "Romi"], all.Select(p => p.Name));

        MachineProfile repetido = MachineProfileForm.Build(null, "VMC", CamDialect.Fanuc, "3 eixos", "").Profile!;
        Assert.Contains("Já existe", MachineProfileForm.Save(all, null, repetido, active).Error);
        Assert.Equal(2, all.Count);

        // renomear o ativo leva o nome junto; editar outro não muda o ativo
        MachineProfile renamed = MachineProfileForm.Build(all[1], "Romi D800", CamDialect.Fanuc, "4º eixo (A)", "6000").Profile!;
        Assert.Equal(("Romi D800", (string?)null), MachineProfileForm.Save(all, 1, renamed, active));
        MachineProfile vmc = MachineProfileForm.Build(all[0], "VMC", CamDialect.Siemens, "3 eixos", "").Profile!;
        Assert.Equal(("Romi D800", (string?)null), MachineProfileForm.Save(all, 0, vmc, "Romi D800"));

        // o que vai para o config.json lê de volta igual
        foreach (MachineProfile p in all)
        {
            Assert.Equal(MachineProfileJson.Serialize(p), MachineProfileJson.Serialize(MachineProfileJson.Parse(MachineProfileJson.Serialize(p)).Profile!));
        }
    }
}
