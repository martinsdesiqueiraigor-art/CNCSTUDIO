namespace CncStudio.Core.Tests;

/// <summary>DESK-01: o programa do CAM ganhou a linha do bruto; testes de "o programa não muda" comparam sem ela.</summary>
internal static class StockLine
{
    public static string Without(string program) =>
        string.Join("\n", program.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart('(', ';', ' ').StartsWith("BRUTO ", StringComparison.Ordinal)));
}
