using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>UX-04g PR B: zero da peça (9 pontos ou origem do desenho; Z0 no topo ou na base), sem padrão escondido.</summary>
public class CamJobZeroTests
{
    private static readonly CamLevels Levels = new(50, 2, 0, -5, 5);

    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Dir, file)).Replace("\r\n", "\n");

    // peça 60 × 40 com canto em (10, 5): caixa X 10..70, Y 5..45; contorno, bolsão (com arcos) e furação
    private static CamJob Job(JobZero? zero, double? height = 20, CamDialect dialect = CamDialect.Fanuc)
    {
        var job = new CamJob { Name = "PECA", Dialect = dialect, ProgramNumber = 1001, WorkOffset = 54, Zero = zero, PartHeight = height, Fixture = new JobFixture("morsa") };
        job.SetStock(new JobStock(5, 0, 70, 50, 20), null);
        job.Contours.Add(Contour.Rectangle(10, 5, 60, 40));
        job.Contours.Add(Contour.Circle(new Point2(40, 25), 12));
        var tool = new CamTool(1, 10, 3000, 500, 200, "FRESA");
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, tool, Levels) { ContourIndex = 0, ContourParams = new ContourParams(ContourSide.Outside, true, false, 0, 3) });
        job.Operations.Add(new CamOperationSpec(JobOpKind.Pocket, tool, Levels) { ContourIndex = 1, PocketParams = new PocketParams(60, 0.2, 2) });
        job.Operations.Add(new CamOperationSpec(JobOpKind.Drilling, new CamTool(2, 6, 4000, 400, 150), Levels) { Drill = new DrillSpec(DrillCycleKind.Peck, 2, 0) });
        return job;
    }

    public static TheoryData<ZeroPoint, double, double> Points => new()
    {
        { ZeroPoint.XMinYMin, 10, 5 }, { ZeroPoint.XMidYMin, 40, 5 }, { ZeroPoint.XMaxYMin, 70, 5 },
        { ZeroPoint.XMinYMid, 10, 25 }, { ZeroPoint.Center, 40, 25 }, { ZeroPoint.XMaxYMid, 70, 25 },
        { ZeroPoint.XMinYMax, 10, 45 }, { ZeroPoint.XMidYMax, 40, 45 }, { ZeroPoint.XMaxYMax, 70, 45 },
        { ZeroPoint.Drawing, 0, 0 },
    };

    [Fact]
    public void Trabalho_novo_nasce_sem_zero()
    {
        Assert.Null(new CamJob().Zero);
        Assert.Equal([CamJobZero.Missing], CamJobZero.Problems(new CamJob()));
    }

    [Theory]
    [InlineData("fresa-zero-centro-base")]
    [InlineData("fresa-zero-g55-canto")]
    public void Referencias_com_zero_geram_o_texto_guardado_e_salvam_igual(string name)
    {
        string json = Read(name + ".casillas-job");
        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        JobBuild build = CamJobBuilder.Build(loaded.Job!, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        string[] expected = Read(name + ".nc").TrimEnd().Split('\n');
        string[] actual = build.Program.Replace("\r\n", "\n").TrimEnd().Split('\n');
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(expected[i] == actual[i], $"linha {i + 1}: esperado \"{expected[i]}\", veio \"{actual[i]}\"");
        }

        Assert.Equal(json, CamJobJson.ToJson(loaded.Job!).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Referencias_novas_sao_as_antigas_deslocadas()
    {
        // centro de 0..60 × 0..40 = (30, 20), Z0 na base de uma peça de 20: X −30, Y −20, Z +20
        string old = Read("fresa-sem-bruto.nc");
        string moved = Read("fresa-zero-centro-base.nc");
        Assert.Contains("\nG0 X-7. Y5.\nG43 H1 Z50.\n", old);
        Assert.Contains("\nG0 X-37. Y-15.\nG43 H1 Z70.\n", moved);
        // G55 no canto inferior direito (60, 0), Z0 no topo: só X desloca
        Assert.Contains("\nG0 X-7. Y5.\n", Read("fresa-fixacao.nc"));
        Assert.Contains("\nG0 X-67. Y5.\n", Read("fresa-zero-g55-canto.nc"));
    }

    [Theory]
    [MemberData(nameof(Points))]
    public void Os_9_pontos_e_a_origem_deslocam_X_e_Y(ZeroPoint point, double x, double y)
    {
        CamJob job = Job(new JobZero(point, ZeroLevel.Top));
        Assert.Equal(new Point3(x, y, 0), CamJobZero.Position(job));
        JobBuild reference = CamJobBuilder.Build(Job(JobZero.Legacy), null);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        IReadOnlyList<CamOperation> moved = CamJobZero.Translate(reference.Operations, new Point3(x, y, 0));
        for (int o = 0; o < moved.Count; o++)
        {
            for (int k = 0; k < moved[o].Moves.Count; k++)
            {
                ToolMove a = reference.Operations[o].Moves[k], b = moved[o].Moves[k];
                Assert.Equal(a.X - x, b.X, 9);
                Assert.Equal(a.Y - y, b.Y, 9);
                Assert.Equal(a.Z, b.Z, 9);
                if (a.IsArc)
                {
                    Assert.Equal(a.Center.X - x, b.Center.X, 9);
                    Assert.Equal(a.Center.Y - y, b.Center.Y, 9);
                }
            }
        }

        DrillCycle d0 = reference.Operations[2].Drill!, d1 = moved[2].Drill!;
        Assert.Equal(d0.Points[0].X - x, d1.Points[0].X, 9);
        Assert.Equal(d0.Points[0].Y - y, d1.Points[0].Y, 9);
        Assert.Equal(d0.BottomZ, d1.BottomZ, 9);
    }

    [Fact]
    public void Origem_do_desenho_no_topo_nao_desloca_e_so_ganha_a_linha_do_zero()
    {
        string legacy = CamJobBuilder.Build(Job(JobZero.Legacy), null).Program;
        string chosen = CamJobBuilder.Build(Job(new JobZero(ZeroPoint.Drawing, ZeroLevel.Top)), null).Program;
        Assert.Equal(legacy, chosen.Replace("(ZERO G54: ORIGEM DO DESENHO, Z0 NO TOPO)\n", ""));
        IReadOnlyList<CamOperation> ops = CamJobBuilder.Build(Job(JobZero.Legacy), null).Operations;
        Assert.Same(ops, CamJobZero.Translate(ops, new Point3(0, 0, 0)));
    }

    [Fact]
    public void Z0_na_base_soma_a_altura_e_o_Z_digitado_segue_medido_do_topo()
    {
        // profundidade 5 numa peça de 20, Z0 na base: sai em Z15 (o aviso do cartão)
        CamJob job = Job(new JobZero(ZeroPoint.Drawing, ZeroLevel.Bottom));
        Assert.Equal(new Point3(0, 0, -20), CamJobZero.Position(job));
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("Z15.", build.Program);
        Assert.Contains("Z70.", build.Program); // Z seguro 50 + 20
        Assert.DoesNotContain("Z-5.", build.Program);
        Assert.Equal(-5, job.Operations[0].Levels.BottomZ); // o trabalho não muda: só o programa
        Assert.Equal(new Point3(40, 25, -20), CamJobZero.Position(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Bottom))));
    }

    [Fact]
    public void Zero_vazio_nao_gera_nem_envia_mas_salva_em_v3_e_reabre_vazio()
    {
        CamJob job = Job(null);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.False(build.Ok);
        Assert.Equal("", build.Program);
        Assert.Contains(CamJobZero.Missing, build.Errors);
        IReadOnlyList<string> pending = CamJobInfo.Pending(job, build);
        Assert.Contains(CamJobZero.Missing, pending);
        Assert.True(CamJobInfo.Blocks(CamJobZero.Missing));
        Assert.True(CamJobZero.IsZeroProblem(CamJobZero.Missing));
        Assert.Empty(CamJobInfo.SaveBlockers(job));

        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 3", json);
        Assert.DoesNotContain("\"zero\"", json);
        CamJob back = CamJobJson.FromJson(json).Job!;
        Assert.Null(back.Zero);
        Assert.Equal(json, CamJobJson.ToJson(back));
    }

    [Fact]
    public void Zero_pela_metade_grava_so_o_que_tem_e_fica_incompleto()
    {
        CamJob job = Job(new JobZero(ZeroPoint.Center, null));
        Assert.Equal([CamJobZero.LevelMissing], CamJobZero.Problems(job));
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"point\": \"Center\"", json);
        Assert.DoesNotContain("\"z\":", json);
        Assert.Equal(job.Zero, CamJobJson.FromJson(json).Job!.Zero);
        Assert.Equal([CamJobZero.PointMissing], CamJobZero.Problems(Job(new JobZero(null, ZeroLevel.Top))));
    }

    [Fact]
    public void Z0_na_base_sem_altura_fica_incompleto_e_no_topo_nao_precisa()
    {
        CamJob job = Job(new JobZero(ZeroPoint.Center, ZeroLevel.Bottom), height: null);
        Assert.Equal([CamJobZero.HeightMissing], CamJobZero.Problems(job));
        Assert.Contains(CamJobZero.HeightMissing, CamJobBuilder.Build(job, null).Errors);
        Assert.Null(CamJobZero.Position(job));
        Assert.Empty(CamJobZero.Problems(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Top), height: null)));
        Assert.Single(CamJobZero.Problems(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Bottom), height: 0)));
    }

    [Theory]
    [InlineData("fresa-sem-bruto")]
    [InlineData("fresa-fixacao")]
    [InlineData("fresa-3mais2")]
    public void Arquivo_antigo_abre_como_antes_gera_igual_e_salva_sem_campo(string name)
    {
        string json = Read(name + ".casillas-job");
        CamJob job = CamJobJson.FromJson(json).Job!;
        Assert.Equal(JobZero.Legacy, job.Zero);
        Assert.Equal("Origem do desenho (como antes)", CamJobZero.Summary(job));
        Assert.Null(CamJobZero.HeaderNote(job));
        Assert.Empty(CamJobZero.Problems(job));
        string again = CamJobJson.ToJson(job).Replace("\r\n", "\n");
        Assert.Equal(json, again);
        Assert.DoesNotContain("zero", again);
    }

    [Fact]
    public void Arquivo_antigo_que_sobe_para_v3_por_outro_motivo_grava_como_antes()
    {
        // decisão do Igor (08/11): a v3 sem o campo seria lida como vazio, então grava "comoAntes"
        CamJob job = CamJobJson.FromJson(Read("fresa-sem-bruto.casillas-job")).Job!;
        job.ProgramNumber = null;
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 3", json);
        Assert.Contains("\"zero\": \"comoAntes\"", json);
        CamJob back = CamJobJson.FromJson(json).Job!;
        Assert.Equal(JobZero.Legacy, back.Zero);
        back.ProgramNumber = 1001;
        Assert.Equal(Read("fresa-sem-bruto.casillas-job"), CamJobJson.ToJson(back).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Escolher_outro_zero_em_arquivo_antigo_passa_a_gravar_o_campo()
    {
        CamJob job = CamJobJson.FromJson(Read("fresa-sem-bruto.casillas-job")).Job!;
        job.Zero = new JobZero(ZeroPoint.Drawing, ZeroLevel.Top);
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 3", json);
        Assert.Contains("\"point\": \"Drawing\"", json);
    }

    [Fact]
    public void Face_girada_texto_no_cilindro_e_3mais2_so_aceitam_a_origem()
    {
        CamJob face = Job(new JobZero(ZeroPoint.Center, ZeroLevel.Top));
        face.Operations[0] = face.Operations[0] with { Face = new RotaryFaceSpec("A", 90, 25) };
        Assert.Equal(["face girada"], CamJobZero.RotaryUses(face));

        CamJob tilted = Job(new JobZero(ZeroPoint.Center, ZeroLevel.Top));
        tilted.Operations[0] = tilted.Operations[0] with { Face = new RotaryFaceSpec("A", 30, 25) { Axis2 = "C" } };
        Assert.Equal(["3+2"], CamJobZero.RotaryUses(tilted));

        CamJob wrap = CamJobJson.FromJson(Read("fresa-texto-cilindro.casillas-job")).Job!;
        Assert.Contains("texto no cilindro", CamJobZero.RotaryUses(wrap));
        wrap.Zero = new JobZero(ZeroPoint.Center, ZeroLevel.Top);

        CamJob ref32 = CamJobJson.FromJson(Read("fresa-3mais2.casillas-job")).Job!;
        Assert.Contains("3+2", CamJobZero.RotaryUses(ref32));
        ref32.Zero = new JobZero(ZeroPoint.Drawing, ZeroLevel.Bottom);
        ref32.PartHeight = 20;

        foreach (CamJob job in new[] { face, tilted, wrap, ref32 })
        {
            Assert.True(CamJobZero.OnlyDrawing(job));
            Assert.Contains(CamJobZero.RotaryBlocked, CamJobZero.Problems(job));
            Assert.Contains(CamJobZero.RotaryBlocked, CamJobInfo.Pending(job, CamJobBuilder.Build(job, null)));
            // nada é trocado sozinho
            Assert.NotEqual(new JobZero(ZeroPoint.Drawing, ZeroLevel.Top), job.Zero);
            job.Zero = new JobZero(ZeroPoint.Drawing, ZeroLevel.Top);
            Assert.DoesNotContain(CamJobZero.RotaryBlocked, CamJobZero.Problems(job));
        }

        // operação desligada não conta; "como antes" nunca é bloqueado
        face.Operations[0] = face.Operations[0] with { Enabled = false };
        Assert.False(CamJobZero.OnlyDrawing(face));
        Assert.Empty(CamJobZero.Problems(CamJobJson.FromJson(Read("fresa-3mais2.casillas-job")).Job!));
    }

    [Fact]
    public void Linha_do_zero_no_cabecalho_so_quando_escolhido()
    {
        string fanuc = CamJobBuilder.Build(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Top)), null).Program;
        Assert.Contains("\n(GERADO PELO CASILLAS CAM - SIMULE ANTES DE RODAR NA MAQUINA)\nG21 G17 G40 G49 G80 G90 G94\n(ZERO G54: CENTRO DA PECA, Z0 NO TOPO)\nG54\n", fanuc);
        string siemens = CamJobBuilder.Build(Job(new JobZero(ZeroPoint.XMinYMin, ZeroLevel.Bottom), dialect: CamDialect.Siemens), null).Program;
        Assert.Contains("\n; ZERO G54: CANTO INFERIOR ESQUERDO DA PECA, Z0 NA BASE\nG54\n", siemens);
        Assert.DoesNotContain("ZERO", CamJobBuilder.Build(Job(JobZero.Legacy), null).Program);
        Assert.DoesNotContain("ZERO", Read("fresa-sem-bruto.nc"));
    }

    [Theory]
    [MemberData(nameof(Points))]
    public void Setas_do_3D_saem_do_zero_escolhido(ZeroPoint point, double x, double y)
    {
        foreach ((ZeroLevel level, double z) in new[] { (ZeroLevel.Top, 0.0), (ZeroLevel.Bottom, -20.0) })
        {
            CamJob job = Job(new JobZero(point, level));
            Point3 zero = CamJobZero.Position(job)!.Value;
            Assert.Equal(new Point3(x, y, z), zero);
            SceneSnapshot scene = CamJobScene.BuildResolved(null, job.Contours, null, CamViewOptions.Default with { Axes = true }, zero: zero);
            List<SceneLinesState> axes = scene.Lines.Where(l => l.Role == SceneRole.Axis).ToList();
            Assert.Equal(3, axes.Count);
            Assert.All(axes, a => Assert.Equal(new[] { (float)x, (float)y, (float)z }, a.Positions[..3]));
        }
    }

    [Fact]
    public void Trajetoria_do_3D_volta_ao_referencial_da_peca()
    {
        // a trajetória lida do programa deslocado fica no mesmo lugar da lida do programa "como antes"
        string legacy = CamJobBuilder.Build(Job(JobZero.Legacy), null).Program;
        CamJob job = Job(new JobZero(ZeroPoint.Center, ZeroLevel.Bottom));
        string moved = CamJobBuilder.Build(job, null).Program;
        var options = CamViewOptions.Default with { Path = true };
        float[] a = Feed(CamJobScene.BuildResolved(null, job.Contours, ProgramAnalyzer.Analyze(legacy, Dialects.Dialect.Fanuc).ToolPath, options));
        float[] b = Feed(CamJobScene.BuildResolved(null, job.Contours, ProgramAnalyzer.Analyze(moved, Dialects.Dialect.Fanuc).ToolPath, options, zero: CamJobZero.Position(job)!.Value));
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i], 3);
        }

        static float[] Feed(SceneSnapshot s) => s.Lines.Single(l => l.Role == SceneRole.FeedPath).Positions;
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Percurso_simulado_bate_com_o_gerado_e_deslocado(CamDialect dialect)
    {
        // o simulador do Code roda com a tabela de zeros vazia: o percurso sai no referencial do programa (o zero escolhido)
        CamJob job = Job(new JobZero(ZeroPoint.XMaxYMax, ZeroLevel.Bottom), dialect: dialect);
        Point3 zero = CamJobZero.Position(job)!.Value;
        Dialects.Dialect d = dialect == CamDialect.Siemens ? Dialects.Dialect.Siemens : Dialects.Dialect.Fanuc;
        var legacy = ProgramAnalyzer.Analyze(CamJobBuilder.Build(Job(JobZero.Legacy, dialect: dialect), null).Program, d).ToolPath;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        var analysis = ProgramAnalyzer.Analyze(build.Program, d);
        List<Move> a = legacy.Moves.Where(m => m.Motion != MotionMode.Rapid).ToList();
        List<Move> b = analysis.ToolPath.Moves.Where(m => m.Motion != MotionMode.Rapid).ToList();
        Assert.NotEmpty(a);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].End.X - zero.X, b[i].End.X, 3);
            Assert.Equal(a[i].End.Y - zero.Y, b[i].End.Y, 3);
            Assert.Equal(a[i].End.Z - zero.Z, b[i].End.Z, 3);
        }

        // e o simulador roda o programa deslocado até o fim
        var parts = Simulation.SimulationFactory.Create(analysis, null, build.Program);
        parts.Simulation.RunToEnd();
        Assert.NotNull(parts.Map);
    }

    [Fact]
    public void Leitor_recusa_versao_maior_e_abre_v3_com_e_sem_zero()
    {
        string json = CamJobJson.ToJson(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Top)));
        Assert.False(CamJobJson.FromJson(json.Replace("\"version\": 3", "\"version\": 4")).Ok);
        Assert.Equal(new JobZero(ZeroPoint.Center, ZeroLevel.Top), CamJobJson.FromJson(json).Job!.Zero);
        string noZero = CamJobJson.ToJson(Job(null));
        JobLoadResult empty = CamJobJson.FromJson(noZero);
        Assert.True(empty.Ok, empty.Error);
        Assert.Null(empty.Job!.Zero);
        Assert.False(CamJobJson.FromJson(json.Replace("\"Center\"", "\"Meio\"")).Ok);
        Assert.False(CamJobJson.FromJson(json.Replace("\"Center\"", "\"7\"")).Ok);
    }

    [Fact]
    public void Textos_do_cartao_e_da_arvore()
    {
        Assert.Equal("Zero em X 40 · Y 25 do desenho, Z0 no topo", CamJobZero.Summary(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Top))));
        Assert.Equal("Origem G54: centro da peça, Z0 na base", CamJobInfo.OriginTitle(Job(new JobZero(ZeroPoint.Center, ZeroLevel.Bottom))));
        Assert.Equal("Origem G54: zero da peça (falta escolher)", CamJobInfo.OriginTitle(Job(null)));
        Assert.Equal("Origem G54: X0 Y0 do desenho, topo Z0", CamJobInfo.OriginTitle(Job(JobZero.Legacy)));
    }
}
