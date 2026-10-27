using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class MachineProfileTests
{
    [Fact]
    public void ModelosProntosSaoValidosEDescritosCertoPelaConfiguracao()
    {
        Assert.Equal(MachineConfiguration.ThreeAxis, MachineProfile.ThreeAxis().Configuration);
        Assert.Equal(MachineConfiguration.FourAxisTable, MachineProfile.FourAxisTableA().Configuration);
        Assert.Equal(MachineConfiguration.FiveAxisTableTable, MachineProfile.FiveAxisTableTable().Configuration);
        MachineProfile mixed = MachineProfile.FiveAxisMixed();
        Assert.Equal(MachineConfiguration.FiveAxisMixed, mixed.Configuration);
        Assert.Equal("5 eixos mista: cabeçote B + mesa C", mixed.Describe());
        foreach (MachineProfile p in new[] { MachineProfile.ThreeAxis(), MachineProfile.FourAxisTableA(), MachineProfile.FiveAxisTableTable(), mixed })
        {
            Assert.Empty(p.Validate());
        }
    }

    [Fact]
    public void LetraDefineOEixoLinearDeGiro()
    {
        Assert.Equal("X", new RotaryAxisConfig { Letter = "A" }.LinearAxis);
        Assert.Equal("Y", new RotaryAxisConfig { Letter = "B" }.LinearAxis);
        Assert.Equal("Z", new RotaryAxisConfig { Letter = "C" }.LinearAxis);
    }

    [Fact]
    public void RecusaLetraRepetidaLimitesInvertidosENomeVazio()
    {
        var p = MachineProfile.FiveAxisTableTable();
        p.Axes[1].Letter = "A";
        Assert.Contains(p.Validate(), e => e.Contains("duas vezes"));

        p = MachineProfile.FourAxisTableA();
        p.Axes[0].MinAngle = 10;
        p.Axes[0].MaxAngle = -10;
        Assert.Contains(p.Validate(), e => e.Contains("mínimo"));

        p = MachineProfile.ThreeAxis(" ");
        Assert.Contains(p.Validate(), e => e.Contains("nome"));

        p = MachineProfile.FourAxisTableA();
        p.Axes[0].Letter = "X";
        Assert.Contains(p.Validate(), e => e.Contains("inválido"));
    }

    [Fact]
    public void RecusaMaisDeDoisEixosRotativos()
    {
        var p = MachineProfile.FiveAxisTableTable();
        p.Axes.Add(new RotaryAxisConfig { Letter = "B" });
        Assert.Contains(p.Validate(), e => e.Contains("No máximo"));
    }

    [Fact]
    public void ModoFuncaoDaMaquinaAvisaSobreOpcionalNoFanuc()
    {
        var p = MachineProfile.FiveAxisMixed();
        Assert.Empty(p.Warnings());
        p.Mode = RotaryOutputMode.Native;
        Assert.Contains(p.Warnings(), w => w.Contains("21i"));
        p.Controller = CamDialect.Siemens;
        Assert.Contains(p.Warnings(), w => w.Contains("CYCLE800"));
    }

    [Fact]
    public void AvisaQuandoOEixoNaoPassaPorZero()
    {
        var p = MachineProfile.FourAxisTableA();
        p.Axes[0].MinAngle = 10;
        p.Axes[0].MaxAngle = 90;
        Assert.Contains(p.Warnings(), w => w.Contains("zero"));
    }

    [Fact]
    public void JsonDeIdaEVoltaMantemTudo()
    {
        var p = MachineProfile.FiveAxisMixed("Meu centro");
        p.Controller = CamDialect.Siemens;
        p.Axes[0].Reversed = true;
        p.Axes[1].PivotX = 123.456;
        p.Axes[1].PivotZ = -250;
        string json = MachineProfileJson.Serialize(p);
        Assert.Contains("\"cabecote\"", json);
        Assert.Contains("\"Siemens\"", json);
        (MachineProfile? back, string? error) = MachineProfileJson.Parse(json);
        Assert.Null(error);
        Assert.Equal("Meu centro", back!.Name);
        Assert.Equal(CamDialect.Siemens, back.Controller);
        Assert.True(back.Axes[0].Reversed);
        Assert.Equal(RotaryCarrier.Head, back.Axes[0].Carrier);
        Assert.Equal(123.456, back.Axes[1].PivotX);
        Assert.Equal(-250, back.Axes[1].PivotZ);
        Assert.Equal(p.Describe(), back.Describe());
    }

    [Fact]
    public void JsonInvalidoOuComEixoErradoVoltaMensagemEmPortugues()
    {
        Assert.NotNull(MachineProfileJson.Parse("{ não é json").Error);
        Assert.NotNull(MachineProfileJson.Parse("null").Error);
        const string bad = """{"Name":"x","Axes":[{"Letter":"Q"}]}""";
        (MachineProfile? p, string? error) = MachineProfileJson.Parse(bad);
        Assert.Null(p);
        Assert.Contains("inválido", error);
    }

    [Fact]
    public void LetraEmMinusculaNoArquivoEAceita()
    {
        const string json = """{"Name":"x","Axes":[{"Letter":"b","Carrier":"cabecote","MinAngle":-90,"MaxAngle":90}]}""";
        (MachineProfile? p, string? error) = MachineProfileJson.Parse(json);
        Assert.Null(error);
        Assert.Equal("B", p!.Axes[0].Letter);
    }

    [Fact]
    public void LimiteDeAnguloEhInclusivo()
    {
        var a = new RotaryAxisConfig { MinAngle = -30, MaxAngle = 110 };
        Assert.True(a.Contains(-30));
        Assert.True(a.Contains(110));
        Assert.False(a.Contains(110.1));
    }
}
