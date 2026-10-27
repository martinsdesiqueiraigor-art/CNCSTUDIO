using System.Text.Json.Nodes;
using CncStudio.Core;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class CamQuickJsonTests
{
    private static JsonObject Defaults()
    {
        var o = new JsonObject();
        foreach (JsonNode? f in JsonNode.Parse(CamQuickJson.Catalog())!.AsArray())
        {
            o[(string)f!["id"]!] = f["def"]!.DeepClone();
        }

        return o;
    }

    private static JsonObject Run(JsonObject v) => JsonNode.Parse(CamQuickJson.Generate(v.ToJsonString()))!.AsObject();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GeraProgramaPadraoNosDoisComandos(int dialect)
    {
        JsonObject v = Defaults();
        v["dialect"] = dialect;
        JsonObject r = Run(v);
        Assert.True((bool)r["ok"]!, r.ToJsonString());
        Assert.Contains("G1", (string)r["program"]!);
    }

    [Fact]
    public void FaceGiradaEscreveAEErroSaiQuandoForaDoCurso()
    {
        JsonObject v = Defaults();
        v["ROT"] = true;
        JsonObject r = Run(v);
        Assert.True((bool)r["ok"]!, r.ToJsonString());
        Assert.Contains("A90", (string)r["program"]!);
        v["ANG"] = 500;
        Assert.False((bool)Run(v)["ok"]!);
    }

    [Fact]
    public void CampoVazioDaErroEmVezDeExcecao()
    {
        JsonObject v = Defaults();
        v["D"] = null;
        Assert.False((bool)Run(v)["ok"]!);
        Assert.False((bool)JsonNode.Parse(CamQuickJson.Generate("lixo"))!["ok"]!);
    }

    [Fact]
    public void LinhaDeGiroSoExisteComFaceGiradaMarcada()
    {
        JsonObject v = Defaults();
        Assert.Null(CamQuickJson.PartFrameOf(v.ToJsonString()));
        v["ROT"] = true;
        v["PIVH"] = 12;
        v["PIVZ"] = -30;
        CncStudio.Core.Scene.RotaryPartFrame? f = CamQuickJson.PartFrameOf(v.ToJsonString());
        Assert.NotNull(f);
        Assert.Equal(12, f!.AY);
        Assert.Equal(-30, f.AZ);
        Assert.Null(CamQuickJson.PartFrameOf("lixo"));
    }
}
