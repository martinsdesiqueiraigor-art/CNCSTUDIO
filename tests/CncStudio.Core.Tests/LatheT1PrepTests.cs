using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T1: Programa nº, G5x e refrigeração digitados, operação nova vazia, bruto do trabalho e Salvar sem trava.</summary>
public class LatheT1PrepTests
{
    private static LatheProfile Profile()
    {
        Point2[] pts = [new(0, 8), new(-2, 10), new(-25, 10), new(-25, 14), new(-45, 14)];
        return LatheProfile.From(Enumerable.Range(1, pts.Length - 1).Select(i => Segment2.Line(pts[i - 1], pts[i])).ToList(), out _)!;
    }

    private static LatheJob BlankWithFacing()
    {
        LatheJob job = LatheJob.Blank("PECA");
        job.Profile = Profile();
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        return job;
    }

    [Fact]
    public void Trabalho_novo_nasce_sem_programa_g5x_e_refrigeracao_e_nao_gera()
    {
        LatheJob job = BlankWithFacing();
        Assert.Null(job.ProgramNumber);
        Assert.Null(job.WorkOffset);
        Assert.Null(job.Coolant);
        Assert.Equal(["Falta o Programa nº", "Falta o G5x", "Falta a refrigeração"], LatheJobProgram.Problems(job));

        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.False(build.Ok);
        Assert.Equal("", build.Program);
        Assert.Equal(["Falta o Programa nº", "Falta o G5x", "Falta a refrigeração"], build.Errors);
        // a operação segue calculada (a árvore mostra Pronta) e as três pendências aparecem na barra
        Assert.True(build.Items[0].Ok);
        Assert.Equal(["Falta o Programa nº", "Falta o G5x", "Falta a refrigeração"], LatheJobInfo.Pending(job, build));
        Assert.Equal("Programa nº: falta · G5x: falta", LatheJobProgram.StatusText(job.Dialect, job.ProgramNumber, job.WorkOffset));
        Assert.Equal("Origem G5x (falta): X0 no centro da peça, Z0 do perfil", LatheJobInfo.OriginTitle(job));
    }

    [Fact]
    public void Digitar_os_tres_gera_o_mesmo_programa_do_trabalho_antigo()
    {
        LatheJob job = BlankWithFacing();
        job.ProgramNumber = 1002;
        job.WorkOffset = 54;
        job.Coolant = true;
        var old = new LatheJob { Name = "PECA", Profile = Profile() };
        old.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Equal(LatheJobBuilder.Build(old).Program, build.Program);

        job.Coolant = false;
        string dry = LatheJobBuilder.Build(job).Program;
        Assert.DoesNotContain("M8", dry);
        Assert.DoesNotContain("M9", dry);
    }

    [Fact]
    public void Siemens_nao_exige_o_numero_do_programa()
    {
        LatheJob job = BlankWithFacing();
        job.Dialect = CamDialect.Siemens;
        job.WorkOffset = 55;
        job.Coolant = true;
        Assert.Empty(LatheJobProgram.Problems(job));
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("G55", build.Program);
        Assert.Equal("G55", LatheJobProgram.StatusText(job.Dialect, job.ProgramNumber, job.WorkOffset));
    }

    [Fact]
    public void Arquivo_novo_nao_grava_chave_vazia_e_abre_vazio()
    {
        LatheJob job = BlankWithFacing();
        string json = LatheJobJson.ToJson(job, keepEmptyFields: true);
        Assert.DoesNotContain("programNumber", json);
        Assert.DoesNotContain("workOffset", json);
        Assert.DoesNotContain("coolant", json);
        Assert.DoesNotContain("stock\"", json.Replace("stockDiameter", "").Replace("stockFaceZ", ""));

        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Null(back.Job!.ProgramNumber);
        Assert.Null(back.Job.WorkOffset);
        Assert.Null(back.Job.Coolant);
        Assert.Null(back.Job.Stock);
        Assert.Equal(json, LatheJobJson.ToJson(back.Job, keepEmptyFields: true));

        // só um dos três preenchido: os outros continuam fora do arquivo
        job.WorkOffset = 56;
        string partial = LatheJobJson.ToJson(job, keepEmptyFields: true);
        Assert.Contains("\"workOffset\": 56", partial);
        Assert.DoesNotContain("programNumber", partial);
        Assert.Equal(56, LatheJobJson.FromJson(partial).Job!.WorkOffset);
    }

    [Fact]
    public void Operacao_nova_nasce_vazia_e_incompleta()
    {
        foreach ((string _, LatheOpKind kind, bool isInternal) in LatheJobInfo.NewKinds)
        {
            LatheOperationSpec s = LatheJobInfo.NewBlank(kind, isInternal);
            Assert.Equal(LatheJobInfo.NumericFields(s), s.MissingFields);
            Assert.Contains(LatheJobInfo.ToolNumberField, s.MissingFields);
            Assert.Contains(LatheJobInfo.MaxRpmField, s.MissingFields);
            Assert.StartsWith("Falta ", LatheJobInfo.MissingError(s));
            // nenhum número de fábrica guardado (nem Ø 40, nem 3000 rpm)
            Assert.Equal(0, s.Tool.MaxRpm);
            Assert.Equal(0, s.Tool.SurfaceSpeed);
            Assert.Equal(0, s.SafeZ);
            if (s.Turning is { } t)
            {
                Assert.Equal(0, t.StockDiameter);
            }

            if (s.Facing is { } f)
            {
                Assert.Equal(0, f.StockDiameter);
            }
        }

        LatheJob job = LatheJob.Blank();
        job.ProgramNumber = 1;
        job.WorkOffset = 54;
        job.Coolant = true;
        job.Operations.Add(LatheJobInfo.NewBlank(LatheOpKind.Facing, false));
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.False(build.Ok);
        Assert.StartsWith("Operação 1: Falta Ferramenta (T)", build.Errors[0]);
    }

    [Fact]
    public void Operacao_nova_herda_a_ferramenta_anterior_e_o_bruto_do_trabalho()
    {
        var tool = new LatheTool(3, "Desbaste", 180, 2500, 0);
        var stock = new LatheJobStock(50, 120, 2);
        LatheOperationSpec turning = LatheJobInfo.NewBlank(LatheOpKind.Turning, false, tool, stock);
        Assert.Equal(tool, turning.Tool);
        Assert.DoesNotContain(LatheJobInfo.ToolNumberField, turning.MissingFields);
        Assert.DoesNotContain(LatheJobInfo.StockField, turning.MissingFields);
        Assert.Equal(50, turning.Turning!.StockDiameter);

        LatheOperationSpec facing = LatheJobInfo.NewBlank(LatheOpKind.Facing, false, null, stock);
        Assert.Equal(50, facing.Facing!.StockDiameter);
        Assert.Equal(2, facing.Facing.StockFaceZ);
        Assert.DoesNotContain(LatheJobInfo.StockFaceZField, facing.MissingFields);
        Assert.Contains("Z da face pronta", facing.MissingFields);

        // interno: o campo é o Ø do pré-furo, que não vem do bruto
        LatheOperationSpec boring = LatheJobInfo.NewBlank(LatheOpKind.Turning, true, null, stock);
        Assert.Contains("Ø do pré-furo", boring.MissingFields);
        Assert.Equal(0, boring.Turning!.StockDiameter);

        // o botão da faixa cria a mesma operação
        CamPaletteItem item = CamPalette.LatheGroups.SelectMany(g => g.Items).Single(i => i.Id == "faceamento");
        Assert.Equal(facing.MissingFields, CamPalette.NewLatheOperation(item, null, stock)!.MissingFields);
    }

    [Fact]
    public void Salvar_com_campo_vazio_guarda_a_lista_e_abre_incompleta()
    {
        LatheJob job = LatheJob.Blank("PECA");
        job.Operations.Add(LatheJobInfo.NewBlank(LatheOpKind.Drilling, false));
        string json = LatheJobJson.ToJson(job, keepEmptyFields: true);
        Assert.Contains("\"missing\"", json);

        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(job.Operations[0].MissingFields, back.Job!.Operations[0].MissingFields);
        Assert.Equal(json, LatheJobJson.ToJson(back.Job, keepEmptyFields: true));

        // digitar tudo tira a lista do arquivo
        LatheOperationSpec filled = back.Job.Operations[0] with { MissingFields = [] };
        back.Job.Operations[0] = filled;
        Assert.DoesNotContain("\"missing\"", LatheJobJson.ToJson(back.Job, keepEmptyFields: true));
    }

    [Fact]
    public void Bruto_do_trabalho_vai_para_o_arquivo_so_com_o_que_foi_digitado()
    {
        var job = new LatheJob { Stock = new LatheJobStock(Diameter: 50) };
        string json = LatheJobJson.ToJson(job);
        Assert.Contains("\"diameter\": 50", json);
        Assert.DoesNotContain("\"length\"", json);
        Assert.DoesNotContain("faceZ", json);
        Assert.Equal(new LatheJobStock(50), LatheJobJson.FromJson(json).Job!.Stock);

        job.Stock = new LatheJobStock(50, 120.5, -1);
        LatheJob back = LatheJobJson.FromJson(LatheJobJson.ToJson(job)).Job!;
        Assert.Equal(job.Stock, back.Stock);
        Assert.Equal("Bruto do trabalho: Ø 50 × 120,5 mm · face em Z -1", LatheJobInfo.JobStockTitle(back.Stock));
        Assert.Equal("Bruto do trabalho: Ø 50 × ? mm · face em Z ?", LatheJobInfo.JobStockTitle(new LatheJobStock(50)));
        Assert.Null(LatheJobInfo.JobStockTitle(null));
        Assert.Null(LatheJobInfo.JobStockTitle(new LatheJobStock()));

        Assert.NotNull(new LatheJobStock(0).Validate());
        Assert.NotNull(new LatheJobStock(10, -5).Validate());
        Assert.Null(new LatheJobStock(10, 5, -3).Validate());
        Assert.False(LatheJobJson.FromJson(json.Replace("\"diameter\": 50", "\"diameter\": 0")).Ok);
    }

    [Fact]
    public void Bruto_do_trabalho_nao_muda_o_programa_nem_as_operacoes_antigas()
    {
        var job = new LatheJob { Name = "PECA", Profile = Profile() };
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        string before = LatheJobBuilder.Build(job).Program;
        job.Stock = new LatheJobStock(60, 200, 3);
        Assert.Equal(before, LatheJobBuilder.Build(job).Program);
        Assert.Equal(40, job.Operations[0].Facing!.StockDiameter);
    }

    [Fact]
    public void Aviso_de_valor_do_arquivo_fora_da_lista_nao_trava()
    {
        Assert.Empty(LatheJobProgram.Warnings(CamDialect.Fanuc, 1002, 54));
        Assert.Single(LatheJobProgram.Warnings(CamDialect.Siemens, null, 58));
        Assert.Single(LatheJobProgram.Warnings(CamDialect.Fanuc, 12345, 54));
        Assert.True(LatheJobProgram.IsProgramProblem(LatheJobProgram.CoolantMissing));
        Assert.True(LatheJobProgram.IsProgramProblem(CamJobProgram.WorkOffsetMissing));
        Assert.False(LatheJobProgram.IsProgramProblem("Nenhuma operação"));
    }
}
