using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using CncStudio.Core.Editing;

namespace CncStudio.App.Controls;

/// <summary>
/// Barra de procurar/substituir do editor, em português. A lógica de busca fica no Core
/// (<see cref="TextSearch"/>); aqui só a seleção no editor e os botões.
/// </summary>
public partial class SearchBar : UserControl
{
    private TextEditor? _editor;

    public SearchBar()
    {
        InitializeComponent();
        IsVisible = false;

        NextButton.Click += (_, _) => FindNext();
        PreviousButton.Click += (_, _) => FindPrevious();
        CloseButton.Click += (_, _) => Close();
        ReplaceButton.Click += (_, _) => ReplaceCurrent();
        ReplaceAllButton.Click += (_, _) => ReplaceAll();

        FindBox.TextChanged += (_, _) => UpdateCount();
        MatchCaseCheck.IsCheckedChanged += (_, _) => UpdateCount();
        WholeWordCheck.IsCheckedChanged += (_, _) => UpdateCount();
        RegexCheck.IsCheckedChanged += (_, _) => UpdateCount();

        FindBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) FindPrevious(); else FindNext();
                e.Handled = true;
            }
        };
        ReplaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ReplaceCurrent();
                e.Handled = true;
            }
        };
    }

    /// <summary>Editor em que a barra procura.</summary>
    public void Attach(TextEditor editor) => _editor = editor;

    private SearchOptions Options => new(
        MatchCase: MatchCaseCheck.IsChecked == true,
        WholeWord: WholeWordCheck.IsChecked == true,
        UseRegex: RegexCheck.IsChecked == true);

    private string Pattern => FindBox.Text ?? "";

    /// <summary>Abre a barra (com o texto selecionado no editor, se for de uma linha só).</summary>
    public void Open(bool replace)
    {
        if (_editor is null)
        {
            return;
        }

        string selected = _editor.SelectedText;
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n'))
        {
            FindBox.Text = selected;
        }

        ReplaceLabel.IsVisible = replace;
        ReplaceBox.IsVisible = replace;
        ReplaceButtons.IsVisible = replace;
        IsVisible = true;
        FindBox.Focus();
        FindBox.SelectAll();
        UpdateCount();
    }

    public void Close()
    {
        IsVisible = false;
        _editor?.TextArea.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    public void FindNext() => Find(backwards: false);

    public void FindPrevious() => Find(backwards: true);

    private void Find(bool backwards)
    {
        if (_editor is null || Pattern.Length == 0)
        {
            if (!IsVisible) Open(replace: false);
            return;
        }

        string text = _editor.Text;
        int from = backwards ? _editor.SelectionStart : _editor.SelectionStart + Math.Max(1, _editor.SelectionLength);
        if (_editor.SelectionLength == 0 && !backwards)
        {
            from = _editor.CaretOffset;
        }

        SearchMatch? match = TextSearch.FindNext(text, Pattern, Options, Math.Min(from, text.Length), backwards);
        if (match is { } m)
        {
            Select(m);
        }

        UpdateCount();
    }

    private void Select(SearchMatch match)
    {
        if (_editor is null)
        {
            return;
        }

        _editor.Select(match.Offset, match.Length);
        _editor.TextArea.Caret.Offset = match.Offset + match.Length;
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(match.Offset).LineNumber);
    }

    /// <summary>Substitui a ocorrência selecionada (se a seleção for uma) e vai para a próxima.</summary>
    private void ReplaceCurrent()
    {
        if (_editor is null || Pattern.Length == 0)
        {
            return;
        }

        List<SearchMatch> matches = TextSearch.FindAll(_editor.Text, Pattern, Options);
        int start = _editor.SelectionStart;
        int length = _editor.SelectionLength;
        bool selectionIsMatch = matches.Exists(m => m.Offset == start && m.Length == length);

        if (selectionIsMatch)
        {
            string replacement = TextSearch.ReplacementFor(_editor.SelectedText, Pattern, ReplaceBox.Text ?? "", Options);
            _editor.Document.Replace(start, length, replacement);
            _editor.Select(start + replacement.Length, 0);
            _editor.TextArea.Caret.Offset = start + replacement.Length;
        }

        FindNext();
    }

    private void ReplaceAll()
    {
        if (_editor is null || Pattern.Length == 0)
        {
            return;
        }

        (string text, int count) = TextSearch.ReplaceAll(_editor.Text, Pattern, ReplaceBox.Text ?? "", Options);
        if (count > 0)
        {
            int caret = Math.Min(_editor.CaretOffset, text.Length);
            _editor.Document.Replace(0, _editor.Document.TextLength, text); // um único "desfazer"
            _editor.TextArea.Caret.Offset = caret;
        }

        CountText.Text = count == 0 ? "nada encontrado" : $"{count} substituído(s)";
    }

    /// <summary>"3 de 12" (ou o erro da expressão regular).</summary>
    private void UpdateCount()
    {
        if (_editor is null || Pattern.Length == 0)
        {
            CountText.Text = "";
            return;
        }

        if (TextSearch.BuildRegex(Pattern, Options, out string? error) is null)
        {
            CountText.Text = "expressão inválida";
            ToolTip.SetTip(CountText, error);
            return;
        }

        ToolTip.SetTip(CountText, null);
        List<SearchMatch> matches = TextSearch.FindAll(_editor.Text, Pattern, Options);
        int current = matches.FindIndex(m => m.Offset == _editor.SelectionStart && m.Length == _editor.SelectionLength);
        CountText.Text = matches.Count == 0
            ? "nada encontrado"
            : current >= 0 ? $"{current + 1} de {matches.Count}" : $"{matches.Count} encontrado(s)";
    }
}
