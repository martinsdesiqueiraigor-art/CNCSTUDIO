namespace CncStudio.Core.Lexing;

/// <summary>Categorias de token. Também definem as cores do syntax highlighting.</summary>
public enum TokenKind
{
    /// <summary>Palavra de endereço: uma letra + número (G01, X10.5, F300, Y=-2).</summary>
    Word,

    /// <summary>Número de bloco (N10).</summary>
    BlockNumber,

    /// <summary>Comentário: "( ... )" no Fanuc, "; ..." no Siemens.</summary>
    Comment,

    /// <summary>Identificador com várias letras (CYCLE81(...), CR=5, MSG(...)). Siemens, em geral.</summary>
    Keyword,

    /// <summary>Marcadores de programa: "%", "O1001" (Fanuc), "%_N_NOME_MPF" (Siemens).</summary>
    ProgramMarker,

    /// <summary>Texto que o lexer não reconhece.</summary>
    Unknown,
}

/// <summary>
/// Um token dentro de uma linha. Guarda coluna e tamanho (e não o texto) para
/// evitar alocações em arquivos grandes; use <see cref="GetText"/> quando precisar do texto.
/// </summary>
/// <param name="Kind">Categoria do token.</param>
/// <param name="Column">Coluna inicial (base 0) na linha.</param>
/// <param name="Length">Quantidade de caracteres.</param>
/// <param name="Letter">Letra do endereço em maiúscula (só para Word/BlockNumber/"O"), senão '\0'.</param>
/// <param name="Value">Valor numérico, quando existir.</param>
public readonly record struct Token(TokenKind Kind, int Column, int Length, char Letter, double? Value)
{
    /// <summary>Recorta o texto do token a partir da linha de origem.</summary>
    public string GetText(string line) => line.Substring(Column, Length);
}
