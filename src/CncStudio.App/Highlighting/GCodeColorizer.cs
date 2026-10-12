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
    // Paleta para tema escuro
    private static readonly IBrush MotionBrush = Brush("#4FC1FF");     // G00–G03
    private static readonly IBrush OtherGBrush = Brush("#569CD6");     // demais G
    private static readonly IBrush MBrush = Brush("#CE9178");          // M
    private static readonly IBrush AxisBrush = Brush("#DCDCAA");       // X Y Z
    private static readonly IBrush ArcBrush = Brush("#C5A5F5");        // I J K R
    private static readonly IBrush FeedSpeedBrush = Brush("#B5CEA8");  // F S
    private static readonly IBrush ToolBrush = Brush("#D7BA7D");       // T D H
    private static readonly IBrush BlockNumberBrush = Brush("#808080"); // N
    private static readonly IBrush CommentBrush = Brush("#6A9955");
    private static readonly IBrush KeywordBrush = Brush("#C586C0");    // CYCLE81, CR=...
    private static readonly IBrush ProgramBrush = Brush("#FFD700");    // %, O1001
    private static readonly IBrush UnknownBrush = Brush("#F44747");

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

    private static IBrush BrushFor(Token token) => token.Kind switch
    {
        TokenKind.Comment => CommentBrush,
        TokenKind.BlockNumber => BlockNumberBrush,
        TokenKind.Keyword => KeywordBrush,
        TokenKind.ProgramMarker => ProgramBrush,
        TokenKind.Unknown => UnknownBrush,
        _ => token.Letter switch
        {
            'G' when token.Value is >= 0 and <= 3 => MotionBrush,
            'G' => OtherGBrush,
            'M' => MBrush,
            'X' or 'Y' or 'Z' => AxisBrush,
            'I' or 'J' or 'K' or 'R' => ArcBrush,
            'F' or 'S' => FeedSpeedBrush,
            'T' or 'D' or 'H' => ToolBrush,
            _ => AxisBrush,
        },
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex)).ToImmutable();
}
