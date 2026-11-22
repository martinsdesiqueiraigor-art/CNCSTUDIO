using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-ADAPT-01c: item da paleta, campos obrigatórios e passe em Z × comprimento de corte no banco.</summary>
public class AdaptiveScreenTests
{
    private static CamOperationSpec Adaptive(double stepDown, int tool = 3) =>
        new CamOperationSpec(JobOpKind.Pocket, new CamTool(tool, 10, 8000, 1500, 300), new CamLevels(20, 2, 0, -12, stepDown))
        {
            PocketParams = new PocketParams(10, 0.3) { Strategy = PocketStrategy.Adaptive, Entry = PocketEntry.Helix, OptimalLoad = 1, HelixDiameter = 6, HelixPitch = 1 },
        };

    private static ToolBank Bank(double? length) =>
        new("b", "Banco", ToolBankMachine.Mill) { Tools = [new BankTool(3) { CuttingLength = length }] };

    [Fact]
    public void PaletteItemMakesAdaptivePocketWithEmptyLoadAndHelix()
    {
        CamPaletteItem item = CamPalette.VisibleGroups.SelectMany(g => g.Items).Single(i => i.Id == CamPalette.AdaptiveId);
        CamOperationSpec s = CamPalette.NewOperation(item, 0)!;

        Assert.Equal(JobOpKind.Pocket, s.Kind);
        Assert.Equal(PocketStrategy.Adaptive, s.PocketParams!.Strategy);
        Assert.Equal(PocketEntry.Helix, s.PocketParams.Entry);
        Assert.Contains(CamJobInfo.AdaptiveLoadField, s.MissingFields);
        Assert.Equal("Desbaste adaptativo", CamJobInfo.KindName(s));
    }

    [Fact]
    public void StepDownLongerThanCuttingLengthIsError()
    {
        (string? error, string? check) = CamJobInfo.AdaptiveDepth(0, Adaptive(12), Bank(10));
        Assert.Contains("maior que o comprimento de corte", error);
        Assert.Null(check);
    }

    [Fact]
    public void StepDownWithinCuttingLengthIsClean()
    {
        Assert.Equal((null, null), CamJobInfo.AdaptiveDepth(0, Adaptive(10), Bank(10)));
    }

    [Fact]
    public void MissingLengthOrToolOutsideBankIsCheck()
    {
        (string? e1, string? c1) = CamJobInfo.AdaptiveDepth(0, Adaptive(6), Bank(null));
        (string? e2, string? c2) = CamJobInfo.AdaptiveDepth(0, Adaptive(6, tool: 9), Bank(10));
        (string? e3, string? c3) = CamJobInfo.AdaptiveDepth(0, Adaptive(6), null);
        Assert.Null(e1); Assert.Null(e2); Assert.Null(e3);
        Assert.Contains("Conferir", c1);
        Assert.Contains("Conferir", c2);
        Assert.Contains("Conferir", c3);
    }

    [Fact]
    public void SwitchingToAdaptiveForcesHelixAndMarksEmptyFields()
    {
        // achado no .exe: Bolsão com entrada Padrão trocado para Adaptativo ficava com a entrada escondida e o cálculo travava
        CamOperationSpec rings = Adaptive(3) with { PocketParams = new PocketParams(60, 0.1) { Entry = PocketEntry.Default } };
        CamOperationSpec a = CamPalette.WithStrategy(rings, PocketStrategy.Adaptive);
        Assert.Equal(PocketStrategy.Adaptive, a.PocketParams!.Strategy);
        Assert.Equal(PocketEntry.Helix, a.PocketParams.Entry);
        Assert.Contains(CamJobInfo.AdaptiveLoadField, a.MissingFields);
        Assert.Contains(CamJobInfo.HelixDiameterField, a.MissingFields);

        // valores já digitados e pré-furo ficam
        CamOperationSpec typed = Adaptive(3) with { PocketParams = Adaptive(3).PocketParams! with { Strategy = PocketStrategy.Rings, Entry = PocketEntry.PreDrill } };
        CamOperationSpec b = CamPalette.WithStrategy(typed, PocketStrategy.Adaptive);
        Assert.Equal(PocketEntry.PreDrill, b.PocketParams!.Entry);
        Assert.Empty(b.MissingFields);

        // sair do adaptativo tira a marca da carga
        Assert.DoesNotContain(CamJobInfo.AdaptiveLoadField, CamPalette.WithStrategy(a, PocketStrategy.Rings).MissingFields);
    }

    [Fact]
    public void OpenBorderWithHelixEntryNeedsNoHelixValues()
    {
        // achado no .exe: o item da paleta nasce em hélice; com borda aberta a hélice vazia não pode travar o Gerar
        Contour stock = Contour.Rectangle(0, 0, 80, 50);
        Contour room = Contour.Rectangle(-25, -25, 130, 100);
        var p = new PocketParams(60, 0.3) { Strategy = PocketStrategy.Adaptive, Entry = PocketEntry.Helix, OptimalLoad = 1, AdaptiveStock = stock };
        OperationResult r = CamOperations.PocketWithIslands(room, [Contour.Circle(new Point2(40, 25), 10)], new CamTool(1, 10, 3000, 1000, 300), new CamLevels(20, 2, 0, -4, 4), p);
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void OtherOperationsAreNotChecked()
    {
        CamOperationSpec ring = Adaptive(50) with { PocketParams = Adaptive(50).PocketParams! with { Strategy = PocketStrategy.Rings } };
        Assert.Equal((null, null), CamJobInfo.AdaptiveDepth(0, ring, Bank(10)));
        Assert.Equal((null, null), CamJobInfo.AdaptiveDepth(0, Adaptive(50) with { Enabled = false }, Bank(10)));
    }
}
