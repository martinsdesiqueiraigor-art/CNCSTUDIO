using System.Globalization;
using System.Text.RegularExpressions;
using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T5: Z0 na face pronta ou na face oposta; sem escolha, o programa sai igual.</summary>
public class LatheT5ZeroTests
{
    private static string Ref(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia", file)).Replace("\r\n", "\n");

    private static string Example(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "exemplos-torno", file)).Replace("\r\n", "\n");

    private static LatheJob Load(string json)
    {
        LatheLoadResult r = LatheJobJson.FromJson(json);
        Assert.True(r.Ok, r.Error);
        return r.Job!;
    }

    private static readonly Regex ZWord = new(@"Z(-?\d+(\.\d*)?)", RegexOptions.Compiled);

    /// <summary>
    /// O programa deslocado é o original com cada Z somado de <paramref name="dz"/> e a linha do Z0 a mais depois do G5x; o resto do
    /// texto (X, avanços, ciclos) não muda.
    /// </summary>
    private static void AssertShifted(string original, string shifted, double dz, string note)
    {
        List<string> a = original.Split('\n').ToList();
        List<string> b = shifted.Split('\n').ToList();
        int g = b.FindIndex(l => l.Contains(note));
        Assert.True(g > 0, "falta a linha do Z0");
        b.RemoveAt(g);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            MatchCollection za = ZWord.Matches(a[i]);
            MatchCollection zb = ZWord.Matches(b[i]);
            Assert.Equal(za.Count, zb.Count);
            Assert.Equal(ZWord.Replace(a[i], "Z#"), ZWord.Replace(b[i], "Z#"));
            for (int k = 0; k < za.Count; k++)
            {
                double va = double.Parse(za[k].Groups[1].Value, CultureInfo.InvariantCulture);
                double vb = double.Parse(zb[k].Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.True(Math.Abs(va + dz - vb) < 1e-3, $"linha {i + 1}: {a[i]} -> {b[i]}");
            }
        }
    }

    [Fact]
    public void Sem_escolha_o_programa_e_o_mesmo_e_o_arquivo_nao_ganha_chave()
    {
        string json = Ref("torno-eixo.casillas-job");
        LatheJob job = Load(json);
        Assert.Null(job.ZeroFace);
        Assert.Equal(0, LatheZero.Shift(job));
        Assert.Equal(json, LatheJobJson.ToJson(job).Replace("\r\n", "\n"));
        Assert.DoesNotContain("Z0 NA FACE", LatheJobBuilder.Build(job).Program);
    }

    [Theory]
    [InlineData(LatheZeroFace.Finished, "Z0 NA FACE PRONTA")]
    [InlineData(LatheZeroFace.Opposite, "Z0 NA FACE OPOSTA")]
    public void Face_escolhida_desloca_todos_os_Z_do_torno_eixo(LatheZeroFace face, string note)
    {
        LatheJob job = Load(Ref("torno-eixo.casillas-job"));
        string original = LatheJobBuilder.Build(job).Program;
        job.ZeroFace = face;
        double faceZ = face == LatheZeroFace.Finished ? job.Profile!.Start.X : job.Profile!.End.X;
        Assert.Equal(faceZ, LatheZero.FaceZ(job));
        LatheBuild shifted = LatheJobBuilder.Build(job);
        Assert.True(shifted.Ok, string.Join("; ", shifted.Errors));
        AssertShifted(original, shifted.Program, -faceZ, note);
    }

    [Theory]
    [InlineData("canal-de-face-fanuc")]
    [InlineData("canal-de-face-siemens")]
    [InlineData("sangramento-fanuc")]
    [InlineData("sangramento-siemens")]
    public void Face_oposta_desloca_canal_de_face_e_sangramento(string name)
    {
        LatheJob job = Load(Example(name + ".casillas-job"));
        Assert.Null(job.Profile);
        // exemplo sem perfil: a face escolhida trava Gerar com o motivo
        job.ZeroFace = LatheZeroFace.Opposite;
        Assert.Contains(LatheZero.NeedsProfile, LatheJobBuilder.Build(job).Errors);

        // com um perfil (Z 3 a -60), o programa sai com todos os Z somados de 60
        job.ZeroFace = null;
        job.Profile = LatheProfile.From([Segment2.Line(new Point2(3, 20), new Point2(-60, 20))], out _);
        string original = LatheJobBuilder.Build(job).Program;
        job.ZeroFace = LatheZeroFace.Opposite;
        AssertShifted(original, LatheJobBuilder.Build(job).Program, 60, "Z0 NA FACE OPOSTA");
        job.ZeroFace = LatheZeroFace.Finished;
        AssertShifted(original, LatheJobBuilder.Build(job).Program, -3, "Z0 NA FACE PRONTA");
    }

    [Fact]
    public void Face_escolhida_sem_perfil_trava_gerar_e_aparece_nas_pendencias()
    {
        var job = new LatheJob { ZeroFace = LatheZeroFace.Finished };
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.Contains(LatheZero.NeedsProfile, build.Errors);
        Assert.Contains(LatheZero.NeedsProfile, LatheJobInfo.Pending(job, build));
        Assert.Contains("Z0 na face pronta (falta o perfil)", LatheJobInfo.OriginTitle(job));
    }

    [Fact]
    public void Z0_vai_e_volta_do_arquivo()
    {
        LatheJob job = Load(Ref("torno-eixo.casillas-job"));
        job.ZeroFace = LatheZeroFace.Opposite;
        string json = LatheJobJson.ToJson(job);
        Assert.Contains("\"zeroFace\": \"face-oposta\"", json);
        LatheJob back = Load(json);
        Assert.Equal(LatheZeroFace.Opposite, back.ZeroFace);
        Assert.Equal(json, LatheJobJson.ToJson(back));
        Assert.False(LatheJobJson.FromJson(json.Replace("face-oposta", "meio")).Ok);
    }
}
