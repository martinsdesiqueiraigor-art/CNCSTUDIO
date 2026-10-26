namespace CncStudio.App.Modules;

/// <summary>Cores do tema CMYK (escuro): mesma paleta da versão web.</summary>
public static class CmykPalette
{
    public static readonly (string Key, string Normal, string Cmyk)[] DarkKeys =
    [
        ("RibbonBackground", "#202329", "#10303B"),
        ("PanelBackground", "#1B1D22", "#0B1418"),
        ("CardBackground", "#23262C", "#13242B"),
        ("StatusBackground", "#1E1E1E", "#081013"),
        ("SubtleBorder", "#3A3F47", "#24414C"),
        ("TabHoverBackground", "#2A2E35", "#17323C"),
        ("TabActiveBackground", "#33404F", "#0B4660"),
        ("RibbonIcon", "#7FB8F0", "#00AEEF"),
    ];
}
