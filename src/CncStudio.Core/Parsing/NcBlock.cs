namespace CncStudio.Core.Parsing;

/// <summary>Palavra de endereço já lida: letra + número ("X10.5", "G01").</summary>
/// <param name="Letter">Letra em maiúscula.</param>
/// <param name="Value">Valor numérico como escrito.</param>
/// <param name="HasDecimalPoint">True se foi escrito com ponto (importa no Fanuc: "X10" pode ser 0,010 mm).</param>
/// <param name="Column">Coluna (base 0) na linha.</param>
/// <param name="Length">Tamanho do texto.</param>
public readonly record struct AddressWord(char Letter, double Value, bool HasDecimalPoint, int Column, int Length);

/// <summary>Tipo de item "não numérico" de um bloco.</summary>
public enum StatementKind
{
    /// <summary>Palavra-chave: CYCLE81(...), TRANS, MCALL, CR=5, IF (...), GOTOF...</summary>
    Keyword,

    /// <summary>Expressão/variável: #1=48, X#7, Z=R1, R1=R1-R3.</summary>
    Expression,

    /// <summary>Rótulo Siemens: "VOLTA:".</summary>
    Label,
}

/// <summary>Item não numérico de um bloco, com o texto original.</summary>
/// <param name="Kind">Tipo.</param>
/// <param name="Name">
/// Keyword: nome em maiúsculas sem argumentos ("CYCLE81", "CR", "TRANS", ",R").
/// Label: nome sem ":". Expression: texto completo.
/// </param>
/// <param name="Value">Valor numérico direto ("CR=5" → 5), quando existir.</param>
/// <param name="Arguments">Texto entre parênteses/colchetes ("10,0,2,-5"), quando existir.</param>
/// <param name="Text">Texto original completo do item.</param>
/// <param name="Column">Coluna (base 0).</param>
/// <param name="Length">Tamanho.</param>
public sealed record Statement(
    StatementKind Kind,
    string Name,
    double? Value,
    string? Arguments,
    string Text,
    int Column,
    int Length);

/// <summary>
/// Um bloco (linha) do programa, só com a sintaxe: o que está escrito, em ordem.
/// O significado (modal, posições, ciclos) é resolvido pelo <c>MachineExecutor</c>.
/// </summary>
public sealed class NcBlock
{
    /// <summary>Linha no arquivo (base 1).</summary>
    public required int LineNumber { get; init; }

    /// <summary>Número N do bloco, se existir.</summary>
    public int? BlockNumber { get; init; }

    /// <summary>Palavras de endereço na ordem em que aparecem.</summary>
    public IReadOnlyList<AddressWord> Words { get; init; } = [];

    /// <summary>Palavras-chave, expressões e rótulos na ordem em que aparecem.</summary>
    public IReadOnlyList<Statement> Statements { get; init; } = [];

    /// <summary>Primeira palavra com a letra, ou null.</summary>
    public AddressWord? Find(char letter)
    {
        foreach (AddressWord word in Words)
        {
            if (word.Letter == letter)
            {
                return word;
            }
        }

        return null;
    }

    public bool Has(char letter) => Find(letter).HasValue;
}
