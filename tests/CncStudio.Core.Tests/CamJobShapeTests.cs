using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Forma desenhada sem DXF no Trabalho fresa (T4a, migrada da aba Fresa antiga).</summary>
public class CamJobShapeTests
{
    [Fact]
    public void Retangulo_e_circulo_validam_medidas()
    {
        Assert.Null(CamJobInfo.RectangleError(0, 0, 60, 40));
        Assert.Null(CamJobInfo.RectangleError(-10, -5, 0.5, 0.5));
        Assert.Contains("maiores que zero", CamJobInfo.RectangleError(0, 0, 0, 40));
        Assert.Contains("maiores que zero", CamJobInfo.RectangleError(0, 0, 60, -1));
        Assert.Contains("números válidos", CamJobInfo.RectangleError(double.NaN, 0, 60, 40));

        Assert.Null(CamJobInfo.CircleError(30, 20, 20));
        Assert.Contains("maior que zero", CamJobInfo.CircleError(30, 20, 0));
        Assert.Contains("números válidos", CamJobInfo.CircleError(30, double.PositiveInfinity, 20));
    }

    [Fact]
    public void Forma_desenhada_entra_no_fim_e_nao_muda_as_operacoes_nem_o_arquivo()
    {
        var job = new CamJob { Name = "FORMA" };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0));
        string before = CamJobBuilder.Build(job, null).Program;

        // mesma geometria da aba antiga: retângulo pelo canto e círculo pelo centro e raio
        job.Contours.Add(Contour.Circle(new Point2(30, 20), 10));
        Assert.Equal(0, job.Operations[0].ContourIndex);
        Assert.Equal(before, CamJobBuilder.Build(job, null).Program);
        Assert.Equal("2 · Círculo Ø20 (20 × 20 mm)", CamJobInfo.ContourTitle(job.Contours[1], 1));

        JobLoadResult again = CamJobJson.FromJson(CamJobJson.ToJson(job));
        Assert.True(again.Ok);
        Assert.Equal(2, again.Job!.Contours.Count);
        Assert.Equal(CamJobJson.ToJson(job), CamJobJson.ToJson(again.Job));
    }

    [Fact]
    public void Pendencia_da_peca_cita_a_forma()
    {
        var job = new CamJob();
        Assert.Contains("Falta a peça (abra um DXF ou desenhe uma forma)", CamJobInfo.Pending(job, CamJobBuilder.Build(job, null)));
    }
}
