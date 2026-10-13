using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace CncStudio.Web;

/// <summary>Arquivo escolhido no aparelho.</summary>
public sealed record PickedFile(string Name, string Text);

/// <summary>
/// Liga um botão do Avalonia a um campo de arquivo nativo (transparente) posicionado por cima dele.
/// O toque do usuário cai direto no campo do navegador, o que é exigido pelo Android para abrir
/// o seletor de arquivos. Os arquivos escolhidos chegam pelo evento <see cref="Picked"/>.
/// </summary>
public sealed class NativeFileButton
{
    private readonly Button _button;
    private readonly string _id;
    private readonly bool _multiple;
    private (double X, double Y, double W, double H, bool Visible) _last = (-1, -1, -1, -1, false);

    public NativeFileButton(Button button, string id, bool multiple)
    {
        _button = button;
        _id = id;
        _multiple = multiple;
        button.LayoutUpdated += (_, _) => Refresh();
        _ = ReceiveAsync();
    }

    public event EventHandler<IReadOnlyList<PickedFile>>? Picked;

    /// <summary>Reposiciona (ou esconde) o campo nativo. Chame ao mostrar/esconder o painel do botão.</summary>
    public void Refresh()
    {
        try
        {
            TopLevel? top = TopLevel.GetTopLevel(_button);
            Point? origin = top is null ? null : _button.TranslatePoint(new Point(0, 0), top);
            bool visible = origin is not null && _button.IsEffectivelyVisible && _button.Bounds.Width > 0;
            var place = origin is { } o && visible
                ? (o.X, o.Y, _button.Bounds.Width, _button.Bounds.Height, true)
                : (0d, 0d, 0d, 0d, false);
            if (place == _last)
            {
                return;
            }

            _last = place;
            WebInterop.PlaceFileInput(_id, _multiple, place.Item1, place.Item2, place.Item3, place.Item4, place.Item5);
        }
        catch
        {
            // fora do navegador ou sem suporte: o botão fica sem o campo nativo
        }
    }

    private async Task ReceiveAsync()
    {
        while (true)
        {
            string? json;
            try
            {
                json = await WebInterop.NextPickedFilesAsync(_id);
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(json))
            {
                continue;
            }

            var files = new List<PickedFile>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    string name = item.GetProperty("name").GetString() ?? "programa.nc";
                    string text = item.GetProperty("text").GetString() ?? "";
                    files.Add(new PickedFile(name, text.Replace("\r\n", "\n")));
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (files.Count > 0)
            {
                Picked?.Invoke(this, files);
            }
        }
    }
}
