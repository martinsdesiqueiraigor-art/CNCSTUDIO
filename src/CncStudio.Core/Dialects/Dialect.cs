namespace CncStudio.Core.Dialects;

/// <summary>Dialetos de G-code suportados.</summary>
public enum Dialect
{
    /// <summary>FANUC (ISO): comentários entre parênteses, programa "O1234".</summary>
    Fanuc,

    /// <summary>Siemens Sinumerik: comentários com ";", endereços com "=", ciclos CYCLExx.</summary>
    Siemens,
}

/// <summary>Detecção simples de dialeto. No protótipo, apenas pela extensão do arquivo.</summary>
public static class DialectDetector
{
    /// <summary>
    /// Retorna o dialeto provável a partir da extensão.
    /// .mpf (programa principal) e .spf (subprograma) são Siemens; o resto é tratado como Fanuc.
    /// </summary>
    public static Dialect FromFileName(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".mpf" or ".spf" => Dialect.Siemens,
            _ => Dialect.Fanuc,
        };
    }
}
