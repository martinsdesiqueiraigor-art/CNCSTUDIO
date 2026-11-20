using Avalonia.Controls;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>
/// Identidade visual do CAM (UI-01, aprovada pelo Igor em 19/11): todas as cores das telas do Trabalho fresa e torno vêm daqui,
/// no código (string ou pincel) e no XAML (<c>{x:Static modules:CamColors.BgBrush}</c>). O CAM é sempre escuro.
/// Regra das cores: laranja = ação e item ativo; azul = seleção e geometria; amarelo = pendência; verde = pronto; vermelho = erro.
/// Fontes: Inter na interface (pacote Avalonia.Fonts.Inter) e mono do sistema nos números de código.
/// </summary>
public static class CamColors
{
    // fundos, do mais escuro ao mais claro
    public const string Chrome = "#111316";
    public const string Bg = "#16181C";
    public const string Panel = "#1B1D22";
    public const string Band = "#202329";
    public const string Card = "#23262C";
    public const string Input = "#141619";
    public const string Viewport = "#1E2025";

    // bordas
    public const string Border = "#2C3037";
    public const string BorderStrong = "#3A3F47";

    // texto
    public const string Text = "#E6E8EB";
    public const string Muted = "#9AA3AE";
    public const string Faint = "#8A929C";

    // ação e item ativo (laranja)
    public const string Accent = "#F08A24";
    public const string AccentText = "#F5A55A";
    public const string AccentBg = "#2E2418";

    // seleção (azul)
    public const string Select = "#4C9BE8";
    public const string SelectBg = "#23303F";
    public const string SelectHover = "#2A3A4C";

    // estados
    public const string Pending = "#E0B040";
    public const string PendingText = "#F0D9A0";
    public const string PendingBg = "#2A2619";
    public const string Ok = "#3DBE6A";
    public const string Error = "#E05555";
    public const string ErrorText = "#FF8A80";

    /// <summary>Fonte da interface do CAM (Inter embutida; cai na fonte do sistema se faltar).</summary>
    public static readonly FontFamily UiFont = new("fonts:Inter#Inter, $Default");

    public static readonly IBrush ChromeBrush = Brush.Parse(Chrome);
    public static readonly IBrush BgBrush = Brush.Parse(Bg);
    public static readonly IBrush PanelBrush = Brush.Parse(Panel);
    public static readonly IBrush BandBrush = Brush.Parse(Band);
    public static readonly IBrush CardBrush = Brush.Parse(Card);
    public static readonly IBrush InputBrush = Brush.Parse(Input);
    public static readonly IBrush ViewportBrush = Brush.Parse(Viewport);
    public static readonly IBrush BorderBrush = Brush.Parse(Border);
    public static readonly IBrush BorderStrongBrush = Brush.Parse(BorderStrong);
    public static readonly IBrush TextBrush = Brush.Parse(Text);
    public static readonly IBrush MutedBrush = Brush.Parse(Muted);
    public static readonly IBrush AccentBrush = Brush.Parse(Accent);
    public static readonly IBrush AccentTextBrush = Brush.Parse(AccentText);
    public static readonly IBrush AccentBgBrush = Brush.Parse(AccentBg);
    public static readonly IBrush SelectBrush = Brush.Parse(Select);
    public static readonly IBrush SelectBgBrush = Brush.Parse(SelectBg);
    public static readonly IBrush SelectHoverBrush = Brush.Parse(SelectHover);
    public static readonly IBrush PendingBrush = Brush.Parse(Pending);
    public static readonly IBrush PendingTextBrush = Brush.Parse(PendingText);
    public static readonly IBrush PendingBgBrush = Brush.Parse(PendingBg);
    public static readonly IBrush ErrorTextBrush = Brush.Parse(ErrorText);

    public static readonly Color SelectBgColor = Color.Parse(SelectBg);
    public static readonly Color SelectHoverColor = Color.Parse(SelectHover);
    public static readonly Color SelectColor = Color.Parse(Select);
    public static readonly Color PanelColor = Color.Parse(Panel);
    public static readonly Color BgColor = Color.Parse(Bg);
    public static readonly Color BorderColor = Color.Parse(Border);

    /// <summary>Botões, caixas e listas do Fluent com a paleta do CAM (cinza-grafite; laranja no botão ligado).</summary>
    public static void ApplyControls(IResourceDictionary r)
    {
        r["ButtonBackground"] = CardBrush;
        r["ButtonBackgroundPointerOver"] = SelectHoverBrush;
        r["ButtonBackgroundPressed"] = SelectBgBrush;
        r["ButtonBorderBrush"] = BorderStrongBrush;
        r["ButtonBorderBrushPointerOver"] = SelectBrush;
        r["ButtonForeground"] = TextBrush;
        r["ButtonForegroundPointerOver"] = TextBrush;
        r["ButtonBackgroundDisabled"] = PanelBrush;
        r["ButtonBorderBrushDisabled"] = BorderBrush;
        r["ButtonForegroundDisabled"] = Brush.Parse(Faint);
        r["ToggleButtonBackground"] = CardBrush;
        r["ToggleButtonBackgroundPointerOver"] = SelectHoverBrush;
        r["ToggleButtonBorderBrush"] = BorderStrongBrush;
        r["ToggleButtonForeground"] = TextBrush;
        r["ToggleButtonBackgroundChecked"] = AccentBgBrush;
        r["ToggleButtonBackgroundCheckedPointerOver"] = AccentBgBrush;
        r["ToggleButtonBackgroundCheckedPressed"] = AccentBgBrush;
        r["ToggleButtonBorderBrushChecked"] = AccentBrush;
        r["ToggleButtonBorderBrushCheckedPointerOver"] = AccentBrush;
        r["ToggleButtonForegroundChecked"] = AccentTextBrush;
        r["ToggleButtonForegroundCheckedPointerOver"] = AccentTextBrush;
        r["TextControlBackground"] = InputBrush;
        r["TextControlBackgroundPointerOver"] = InputBrush;
        r["TextControlBorderBrush"] = BorderStrongBrush;
        r["ComboBoxBackground"] = InputBrush;
        r["ComboBoxBackgroundPointerOver"] = InputBrush;
        r["ComboBoxBorderBrush"] = BorderStrongBrush;
    }
}
