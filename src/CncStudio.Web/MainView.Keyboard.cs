using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using CncStudio.Core.Editing;
using CncStudio.Core.Execution;

namespace CncStudio.Web;

public partial class MainView
{
    private DispatcherTimer? _balloonTimer;

    /// <summary>
    /// Celular: teclado próprio do editor (letras do G-code e números) no lugar do teclado do Android,
    /// e balão com a explicação do código tocado. O editor continua somente leitura para o teclado
    /// do aparelho (nada o abre); as teclas editam o texto direto.
    /// </summary>
    private void InitEditorKeyboard()
    {
        KeyboardToggle.IsVisible = _touch;
        BalloonToggle.IsVisible = _touch;
        UpdateBalloonToggle();
        BalloonToggle.Click += (_, _) =>
        {
            Store("cc.balloon", BalloonOn ? "0" : "1");
            UpdateBalloonToggle();
            if (!BalloonOn)
            {
                HideBalloon();
            }
        };
        KeyboardToggle.Click += (_, _) => SetKeyboardOpen(true);
        Keyboard.KeyPressed += (_, key) => OnEditorKey(key);
        CodeBalloonClose.Click += (_, _) => HideBalloon();

        _balloonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _balloonTimer.Tick += (_, _) => HideBalloon();

        // Tocar num código mostra o balão (só no celular; no PC o cursor é do mouse e do teclado físico)
        if (_touch)
        {
            Editor.TextArea.PointerReleased += (_, _) => Dispatcher.UIThread.Post(() => ShowBalloonAtCaret(0));
        }
    }

    private bool KeyboardOpen => Keyboard.IsVisible;

    /// <summary>Balão do código ligado (padrão) ou desligado pela pessoa; fica guardado no aparelho.</summary>
    private bool BalloonOn => Stored("cc.balloon") != "0";

    private void UpdateBalloonToggle()
    {
        BalloonToggle.Content = BalloonOn ? "Balão: sim" : "Balão: não";
        BalloonToggle.Classes.Set("primary", BalloonOn);
    }

    private void SetKeyboardOpen(bool open)
    {
        Keyboard.IsVisible = open;
        KeyboardToggle.IsVisible = _touch && !open;
        UpdateKeyboardSize();
        if (open)
        {
            Editor.TextArea.Caret.BringCaretToView();
        }
        else
        {
            HideBalloon();
        }
    }

    /// <summary>Teclas mais baixas no celular deitado, para sobrar espaço para o código.</summary>
    private void UpdateKeyboardSize() => Keyboard.SetCompact(Bounds.Height > 0 && Bounds.Height < 500);

    private void OnEditorKey(string key)
    {
        TextDocument doc = Editor.Document;
        int caret = Editor.CaretOffset;
        int selStart = Editor.SelectionStart;
        int selLength = Editor.SelectionLength;

        switch (key)
        {
            case "hide":
                SetKeyboardOpen(false);
                return;
            case "undo":
                if (Editor.CanUndo)
                {
                    StopSimulation();
                    Editor.Undo();
                }

                return;
            case "left":
                Editor.CaretOffset = Math.Max(0, caret - 1);
                break;
            case "right":
                Editor.CaretOffset = Math.Min(doc.TextLength, caret + 1);
                break;
            case "up" or "down":
            {
                int line = Editor.TextArea.Caret.Line + (key == "up" ? -1 : 1);
                if (line >= 1 && line <= doc.LineCount)
                {
                    int column = Editor.TextArea.Caret.Column;
                    Editor.TextArea.Caret.Position = new TextViewPosition(line, Math.Min(column, doc.GetLineByNumber(line).Length + 1));
                }

                break;
            }

            case "back":
                StopSimulation();
                if (selLength > 0)
                {
                    doc.Remove(selStart, selLength);
                    Editor.CaretOffset = selStart;
                }
                else if (caret > 0)
                {
                    // "\r\n" sai junto, de uma vez
                    int count = caret > 1 && doc.GetCharAt(caret - 1) == '\n' && doc.GetCharAt(caret - 2) == '\r' ? 2 : 1;
                    doc.Remove(caret - count, count);
                    Editor.CaretOffset = caret - count;
                }

                break;
            default:
            {
                string text = key switch { "space" => " ", "enter" => "\n", _ => key };
                StopSimulation();
                int start = selLength > 0 ? selStart : caret;
                doc.Replace(start, selLength > 0 ? selLength : 0, text);
                Editor.CaretOffset = start + text.Length;
                Editor.Select(Editor.CaretOffset, 0);
                break;
            }
        }

        Editor.TextArea.Caret.BringCaretToView();
        // Digitando um código (G83...), o balão acompanha o que acabou de ser escrito
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            ShowBalloonAtCaret(1);
        }
        else
        {
            HideBalloon();
        }
    }

    /// <summary>Explica o código no cursor. <paramref name="back"/> = 1 olha o caractere antes do cursor (acabou de digitar).</summary>
    private void ShowBalloonAtCaret(int back)
    {
        if (!BalloonOn)
        {
            return;
        }

        var caret = Editor.TextArea.Caret;
        if (caret.Line < 1 || caret.Line > Editor.Document.LineCount)
        {
            return;
        }

        DocumentLine line = Editor.Document.GetLineByNumber(caret.Line);
        string text = Editor.Document.GetText(line.Offset, line.Length);
        MachineKind machine = _analysis?.ToolPath.Machine ?? MachineKind.Mill;

        // O cursor cai entre dois caracteres: tenta o da direita e depois o da esquerda
        int column = caret.Column - 1;
        string? help = null;
        foreach (int candidate in back == 1 ? new[] { column - 1 } : new[] { column, column - 1 })
        {
            if (candidate >= 0 && candidate < text.Length)
            {
                help = GCodeHelp.Describe(text, candidate, CurrentDialect, machine);
                if (help is not null)
                {
                    break;
                }
            }
        }

        if (help is null)
        {
            HideBalloon();
            return;
        }

        CodeBalloonText.Text = help;
        CodeBalloon.IsVisible = true;
        _balloonTimer?.Stop();
        _balloonTimer?.Start();
    }

    private void HideBalloon()
    {
        _balloonTimer?.Stop();
        CodeBalloon.IsVisible = false;
    }
}
