using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using CncStudio.Core.Dialects;
using CncStudio.Core.Lexing;

namespace CncStudio.App.Highlighting;

/// <summary>
/// Syntax highlighting de G-code. Usa o mesmo lexer do Core, linha a linha,
/// e só para as linhas visíveis — por isso funciona em arquivos grandes.
/// </summary>
public sealed class GCodeColorizer : DocumentColorizingTransformer
{
    /// <summary>Cores de cada tipo de token.</summary>
    private sealed record Palette(
        IBrush Motion, IBrush OtherG, IBrush M, IBrush Axis, IBrush Arc, IBrush FeedSpeed, IBrush Tool,
        IBrush BlockNumber, IBrush Comment, IBrush Keyword, IBrush Program, IBrush Unknown, IBrush Label, IBrush Expression);

    // Tema escuro (cores do VS Code escuro)
    private static readonly Palette Dark = new(
        Motion: Brush("#4FC1FF"),       // G00–G03
        OtherG: Brush("#569CD6"),       // demais G
        M: Brush("#CE9178"),
        Axis: Brush("#DCDCAA"),         // X Y Z
        Arc: Brush("#C5A5F5"),          // I J K R
        FeedSpeed: Brush("#B5CEA8"),    // F S
        Tool: Brush("#D7BA7D"),         // T D H
        BlockNumber: Brush("#808080"),  // N
        Comment: Brush("#6A9955"),
        Keyword: Brush("#C586C0"),      // CYCLE81, CR=...
        Program: Brush("#FFD700"),      // %, O1001
        Unknown: Brush("#F44747"),
        Label: Brush("#4EC9B0"),        // rótulos Siemens
        Expression: Brush("#9CDCFE"));  // #1=..., X[#1], R1=...

    // Tema claro: mesmas ideias, tons mais escuros para fundo branco
    private static readonly Palette Light = new(
        Motion: Brush("#0060C0"),
        OtherG: Brush("#0000C0"),
        M: Brush("#A31515"),
        Axis: Brush("#795E26"),
        Arc: Brush("#7A3EC8"),
        FeedSpeed: Brush("#098658"),
        Tool: Brush("#B5650D"),
        BlockNumber: Brush("#888888"),
        Comment: Brush("#3A8A2A"),
        Keyword: Brush("#AF00DB"),
        Program: Brush("#A07800"),
        Unknown: Brush("#D00000"),
        Label: Brush("#167A6C"),
        Expression: Brush("#001080"));

    private Palette _palette = Dark;

    /// <summary>Usa a paleta do tema claro. Depois de mudar, chame TextView.Redraw().</summary>
    public bool LightTheme
    {
        get => _palette == Light;
        set => _palette = value ? Light : Dark;
    }

    private readonly List<Token> _tokens = [];

    /// <summary>Dialeto atual. Depois de mudar, chame TextView.Redraw().</summary>
    public Dialect Dialect { get; set; } = Dialect.Fanuc;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        GCodeLexer.TokenizeLine(text, Dialect, _tokens);

        foreach (Token token in _tokens)
        {
            IBrush brush = BrushFor(token);
            int start = line.Offset + token.Column;
            int end = start + token.Length;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private IBrush BrushFor(Token token) => token.Kind switch
    {
        TokenKind.Comment => _palette.Comment,
        TokenKind.BlockNumber => _palette.BlockNumber,
        TokenKind.Keyword => _palette.Keyword,
        TokenKind.ProgramMarker => _palette.Program,
        TokenKind.Unknown => _palette.Unknown,
        TokenKind.Label => _palette.Label,
        TokenKind.Expression => _palette.Expression,
        _ => token.Letter switch
        {
            'G' when token.Value is >= 0 and <= 3 => _palette.Motion,
            'G' => _palette.OtherG,
            'M' => _palette.M,
            'X' or 'Y' or 'Z' => _palette.Axis,
            'I' or 'J' or 'K' or 'R' => _palette.Arc,
            'F' or 'S' => _palette.FeedSpeed,
            'T' or 'D' or 'H' => _palette.Tool,
            _ => _palette.Axis,
        },
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex)).ToImmutable();
}
