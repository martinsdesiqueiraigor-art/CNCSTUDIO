using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.App.Dialogs;

/// <summary>
/// Compara dois programas lado a lado. As linhas são alinhadas (o lado que não tem a linha
/// fica em branco), as diferenças ganham cor e as duas colunas rolam juntas.
/// A comparação em si é do Core (<see cref="ProgramDiff"/>).
/// </summary>
public partial class CompareWindow : Window
{
    private readonly string _leftText;
    private readonly Dialect _dialect;
    private string _rightText = "";
    private List<DiffRow> _rows = [];
    private List<int> _blocks = [];      // linha (na grade alinhada) onde começa cada diferença
    private int _currentBlock = -1;
    private bool _syncing;

    // Construtor sem parâmetros exigido pelo carregador de XAML
    public CompareWindow()
        : this("", "", Dialect.Fanuc)
    {
    }

    public CompareWindow(string leftText, string leftName, Dialect dialect)
    {
        InitializeComponent();
        _leftText = leftText;
        _dialect = dialect;
        LeftTitle.Text = $"Esquerda: {leftName} (no editor)";
        RightTitle.Text = "Direita: (escolha um arquivo)";

        IgnoreSpacesCheck.IsCheckedChanged += (_, _) => Recompare();
        IgnoreCaseCheck.IsCheckedChanged += (_, _) => Recompare();
        IgnoreNumbersCheck.IsCheckedChanged += (_, _) => Recompare();
        NextButton.Click += (_, _) => GoToBlock(+1);
        PreviousButton.Click += (_, _) => GoToBlock(-1);
        OpenRightButton.Click += async (_, _) => await ChooseRightFileAsync();

        // As duas colunas rolam juntas
        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => Sync(LeftEditor, RightEditor);
        RightEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => Sync(RightEditor, LeftEditor);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F7)
            {
                GoToBlock(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>Carrega o programa da direita e compara.</summary>
    public void SetRight(string text, string name)
    {
        _rightText = text;
        RightTitle.Text = $"Direita: {name}";
        Recompare();
    }

    /// <summary>Pede o arquivo da direita (se cancelar e não houver nenhum, fecha a janela).</summary>
    public async Task ChooseRightFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Programa para comparar",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Programas CNC") { Patterns = ["*.nc", "*.cnc", "*.tap", "*.mpf", "*.spf", "*.txt", "*.*"] },
            ],
        });

        if (files.Count == 0)
        {
            if (_rightText.Length == 0)
            {
                Close();
            }

            return;
        }

        string? path = files[0].TryGetLocalPath();
        string text = path is not null ? await File.ReadAllTextAsync(path) : "";
        SetRight(text, files[0].Name);
    }

    private void Recompare()
    {
        var options = new DiffOptions(
            IgnoreWhitespace: IgnoreSpacesCheck.IsChecked == true,
            IgnoreCase: IgnoreCaseCheck.IsChecked == true,
            IgnoreBlockNumbers: IgnoreNumbersCheck.IsChecked == true);
        _rows = ProgramDiff.Compare(_leftText, _rightText, options, _dialect);

        string[] left = _leftText.Split('\n');
        string[] right = _rightText.Split('\n');
        LeftEditor.Document = new TextDocument(Aligned(left, r => r.LeftLine));
        RightEditor.Document = new TextDocument(Aligned(right, r => r.RightLine));

        SetRenderer(LeftEditor, isLeft: true);
        SetRenderer(RightEditor, isLeft: false);

        // Começo de cada bloco de diferenças, para navegar
        _blocks = [];
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Kind != DiffKind.Equal && (i == 0 || _rows[i - 1].Kind == DiffKind.Equal))
            {
                _blocks.Add(i);
            }
        }

        _currentBlock = -1;
        int changed = _rows.Count(r => r.Kind == DiffKind.Changed);
        int added = _rows.Count(r => r.Kind == DiffKind.Added);
        int removed = _rows.Count(r => r.Kind == DiffKind.Removed);
        SummaryText.Text = changed + added + removed == 0
            ? "Os programas são iguais."
            : $"{_blocks.Count} trecho(s) diferente(s): {changed} linha(s) alterada(s), {added} incluída(s), {removed} removida(s).";

        if (_blocks.Count > 0)
        {
            GoToBlock(+1);
        }
    }

    /// <summary>Texto alinhado: "   12 │ G01 X10" para linhas existentes, vazio onde o outro lado tem a linha.</summary>
    private string Aligned(string[] lines, Func<DiffRow, int?> lineOf)
    {
        int width = Math.Max(3, lines.Length.ToString().Length);
        var text = new StringBuilder();
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i > 0)
            {
                text.Append('\n');
            }

            if (lineOf(_rows[i]) is { } line && line - 1 < lines.Length)
            {
                text.Append(line.ToString().PadLeft(width)).Append(" │ ").Append(lines[line - 1].TrimEnd('\r'));
            }
        }

        return text.ToString();
    }

    private void SetRenderer(TextEditor editor, bool isLeft)
    {
        IList<IBackgroundRenderer> renderers = editor.TextArea.TextView.BackgroundRenderers;
        foreach (IBackgroundRenderer old in renderers.OfType<DiffRenderer>().ToList())
        {
            renderers.Remove(old);
        }

        renderers.Add(new DiffRenderer(_rows, isLeft));
    }

    private void GoToBlock(int direction)
    {
        if (_blocks.Count == 0)
        {
            return;
        }

        _currentBlock = (_currentBlock + direction + _blocks.Count) % _blocks.Count;
        int row = _blocks[_currentBlock] + 1; // linha do documento (base 1)
        foreach (TextEditor editor in new[] { LeftEditor, RightEditor })
        {
            if (row <= editor.Document.LineCount)
            {
                editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(row).Offset;
            }
        }

        LeftEditor.ScrollToLine(row);
        SummaryText.Text = SummaryText.Text?.Split(" · ")[0] + $" · trecho {_currentBlock + 1} de {_blocks.Count}";
    }

    private void Sync(TextEditor from, TextEditor to)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        to.ScrollToVerticalOffset(from.VerticalOffset);
        _syncing = false;
    }

    /// <summary>Pinta o fundo das linhas conforme a diferença (cada lado só pinta o que tem).</summary>
    private sealed class DiffRenderer(List<DiffRow> rows, bool isLeft) : IBackgroundRenderer
    {
        private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.FromArgb(70, 224, 176, 64)).ToImmutable();
        private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromArgb(80, 224, 85, 85)).ToImmutable();
        private static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromArgb(80, 76, 192, 112)).ToImmutable();
        private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)).ToImmutable();

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid)
            {
                return;
            }

            foreach (VisualLine line in textView.VisualLines)
            {
                int index = line.FirstDocumentLine.LineNumber - 1;
                if (index < 0 || index >= rows.Count)
                {
                    continue;
                }

                IBrush? brush = rows[index].Kind switch
                {
                    DiffKind.Changed => ChangedBrush,
                    DiffKind.Removed => isLeft ? RemovedBrush : EmptyBrush,
                    DiffKind.Added => isLeft ? EmptyBrush : AddedBrush,
                    _ => null,
                };

                if (brush is not null)
                {
                    double top = line.VisualTop - textView.VerticalOffset;
                    drawingContext.FillRectangle(brush, new Rect(0, top, textView.Bounds.Width, line.Height));
                }
            }
        }
    }
}
