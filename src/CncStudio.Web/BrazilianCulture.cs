using System.Globalization;

namespace CncStudio.Web;

/// <summary>
/// Formato brasileiro de números e datas sem depender dos dados de idioma (a versão web usa
/// InvariantGlobalization para ficar menor): vírgula decimal, ponto de milhar, dd/MM/yyyy.
/// </summary>
internal static class BrazilianCulture
{
    public static CultureInfo Instance { get; } = Create();

    private static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        NumberFormatInfo n = culture.NumberFormat;
        n.NumberDecimalSeparator = ",";
        n.NumberGroupSeparator = ".";
        n.PercentDecimalSeparator = ",";
        n.PercentGroupSeparator = ".";
        n.CurrencyDecimalSeparator = ",";
        n.CurrencyGroupSeparator = ".";
        n.CurrencySymbol = "R$";

        DateTimeFormatInfo d = culture.DateTimeFormat;
        d.ShortDatePattern = "dd/MM/yyyy";
        d.LongDatePattern = "dd/MM/yyyy";
        d.ShortTimePattern = "HH:mm";
        d.LongTimePattern = "HH:mm:ss";
        d.FullDateTimePattern = "dd/MM/yyyy HH:mm:ss";
        d.DateSeparator = "/";
        d.TimeSeparator = ":";
        return culture;
    }
}
