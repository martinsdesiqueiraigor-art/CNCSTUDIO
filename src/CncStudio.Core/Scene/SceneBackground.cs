namespace CncStudio.Core.Scene;

/// <summary>
/// Fundo do 3D em degradê vertical (topo → base), nos temas escuro e claro. Uma fonte só das cores:
/// o desktop (OpenGL) e a web (three.js) leem daqui.
/// </summary>
public static class SceneBackground
{
    /// <summary>Cor de cima (RGB 0xRRGGBB).</summary>
    public static uint Top(bool light) => light ? 0xF8FAFCu : 0x273142u;

    /// <summary>Cor de baixo (RGB 0xRRGGBB).</summary>
    public static uint Bottom(bool light) => light ? 0xC9D1DDu : 0x0A0E14u;
}
