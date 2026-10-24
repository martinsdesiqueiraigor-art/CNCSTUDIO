using System.Globalization;
using System.Text;

namespace CncStudio.Core.Optimizer;

/// <summary>One address word as written in the program. Value is the literal number;
/// unit conversion (decimal input, inch) is applied later by <see cref="NcMachine"/>.</summary>
public sealed record NcWord(char Letter, double Value, bool HasDecimalPoint, string Text);

/// <summary>One program line, tokenized without losing the original text.</summary>
public sealed record NcBlock(int Line, string Raw, IReadOnlyList<NcWord> Words,
    string? Comment, bool IsDelimiter, string? Unsupported)
{
    public bool HasCode => Words.Count > 0;
    public bool IsCommentOnly => Words.Count == 0 && Comment is not null && Unsupported is null;
    public IEnumerable<NcWord> All(char letter) => Words.Where(w => w.Letter == letter);
    public NcWord? Get(char letter) => Words.FirstOrDefault(w => w.Letter == letter);
}

/// <summary>Single tokenizer shared by every analysis. Compact code (G0G90X1.), comments,
/// N/O numbers and % are accepted; macro syntax, block delete and malformed text are
/// kept as blocks marked <see cref="NcBlock.Unsupported"/> so nothing is silently dropped.</summary>
public static class NcTokenizer
{
    public static IReadOnlyList<NcBlock> Tokenize(IEnumerable<string> lines) =>
        lines.Select((raw, index) => Tokenize(index + 1, raw)).ToArray();

    public static NcBlock Tokenize(int line, string raw)
    {
        var words = new List<NcWord>();
        var comment = new StringBuilder();
        string? error = null;
        var text = raw.TrimEnd('\r');
        if (text.Trim() == "%") return new(line, raw, words, null, true, null);
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i < text.Length && text[i] == '/') error = "Bloco com barra de supressão (/)";
        while (i < text.Length && error is null)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(')
            {
                var close = text.IndexOf(')', i + 1);
                if (close < 0) { error = "Comentario sem fechamento"; break; }
                if (comment.Length > 0) comment.Append(' ');
                comment.Append(text, i + 1, close - i - 1);
                i = close + 1;
                continue;
            }
            if (c == ';')
            {
                // FANUC uses ';' as end of block; any trailing text is kept as a comment.
                var rest = text[(i + 1)..].Trim();
                if (rest.Length > 0) { if (comment.Length > 0) comment.Append(' '); comment.Append(rest); }
                break;
            }
            if (c == '#' || c == '[' || c == ']') { error = "Sintaxe de macro"; break; }
            var letter = char.ToUpperInvariant(c);
            if (letter < 'A' || letter > 'Z') { error = "Caractere não reconhecido: " + c; break; }
            int j = i + 1;
            while (j < text.Length && text[j] == ' ') j++;
            int start = j;
            if (j < text.Length && (text[j] == '+' || text[j] == '-')) j++;
            int digits = 0; bool point = false;
            while (j < text.Length && (char.IsDigit(text[j]) || (text[j] == '.' && !point)))
            {
                if (text[j] == '.') point = true; else digits++;
                j++;
            }
            if (digits == 0)
            {
                error = j < text.Length && (text[j] == '#' || text[j] == '[') ? "Sintaxe de macro" : "Palavra sem valor numerico: " + letter;
                break;
            }
            var number = text[start..j];
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            { error = "Numero invalido: " + number; break; }
            words.Add(new(letter, value, point, letter + number));
            i = j;
        }
        if (error is null)
            foreach (var g in words.GroupBy(w => w.Letter).Where(g => g.Key is not ('G' or 'M') && g.Count() > 1))
                error = "Endereço repetido: " + g.Key;
        return new(line, raw, words, comment.Length > 0 ? comment.ToString().Trim() : null, false, error);
    }

    /// <summary>Canonical text of a number with an explicit decimal point (e.g. -1.0, 0.25).</summary>
    public static string Decimal(double value)
    {
        var r = Math.Round(value, 6);
        if (r == 0) r = 0;
        return r.ToString("0.0#####", CultureInfo.InvariantCulture);
    }
}
