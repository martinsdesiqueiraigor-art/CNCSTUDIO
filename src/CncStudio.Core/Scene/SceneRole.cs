namespace CncStudio.Core.Scene;

/// <summary>Papel de cada parte da cena: decide a cor e o que a tela pode esconder ou destacar.</summary>
public enum SceneRole
{
    /// <summary>Material da peça (bruto e superfícies usinadas).</summary>
    Material,

    /// <summary>Face de corte da vista em seção (torno com 1/4 removido).</summary>
    Section,

    /// <summary>Fixação: morsa, grampo, placa (base).</summary>
    Fixture,

    /// <summary>Mordentes e castanhas, que encostam na peça.</summary>
    FixtureJaw,

    /// <summary>Parte cortante da ferramenta (ou o inserto, no torno).</summary>
    Tool,

    /// <summary>Porta-ferramenta (ou o suporte do inserto).</summary>
    Holder,

    /// <summary>Trajetória em avanço de trabalho.</summary>
    FeedPath,

    /// <summary>Trajetória em avanço rápido (G00).</summary>
    RapidPath,

    /// <summary>G00 que encostou no material: destacado em vermelho.</summary>
    RapidInMaterial,

    /// <summary>Só o movimento que está sendo executado agora (a trajetória inteira fica escondida por padrão).</summary>
    CurrentPath,
}
