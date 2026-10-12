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

    /// <summary>Identificador com várias letras (CYCLE81(...), CR=5, MSG(...), TRANS, IF, WHILE...).</summary>
    Keyword,

    /// <summary>Marcadores de programa: "%", "O1001" (Fanuc), "%_N_NOME_MPF" (Siemens).</summary>
    ProgramMarker,

    /// <summary>Rótulo de salto do Siemens ("VOLTA:").</summary>
    Label,

    /// <summary>
    /// Expressão ou variável: "#1=48", "X#7", "X[#9+#14]" (Fanuc), "Z=R1", "R1=R1-R3" (Siemens).
    /// Avaliadas pelo interpretador (Fase 1B); por enquanto só reconhecidas.
    /// </summary>
    Expression,

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
/// <param name="Letter">Letra do endereço em maiúscula (Word/BlockNumber/"O"/Expression de endereço), senão '\0'.</param>
/// <param name="Value">Valor numérico, quando existir.</param>
/// <param name="HasDecimalPoint">True se o número foi escrito com ponto ("X10." ou "X10.5").</param>
public readonly record struct Token(
    TokenKind Kind,
    int Column,
    int Length,
    char Letter,
    double? Value,
    bool HasDecimalPoint = false)
{
    /// <summary>Recorta o texto do token a partir da linha de origem.</summary>
    public string GetText(string line) => line.Substring(Column, Length);
}
