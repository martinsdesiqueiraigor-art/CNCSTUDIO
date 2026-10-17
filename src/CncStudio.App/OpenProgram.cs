using AvaloniaEdit.Document;
using CncStudio.Core.Dialects;
using CncStudio.Core.Setup;

namespace CncStudio.App;

/// <summary>
/// Um programa aberto numa aba. O TextDocument guarda o texto e o histórico de desfazer,
/// então trocar de aba não perde nada.
/// </summary>
internal sealed class OpenProgram
{
    public required TextDocument Text { get; init; }

    /// <summary>Caminho no disco (null = ainda não salvo, ou exemplo).</summary>
    public string? FilePath { get; set; }

    public string DisplayName { get; set; } = "sem título";

    public bool IsDirty { get; set; }

    /// <summary>Exemplo da pasta samples: salvar pede um local novo.</summary>
    public bool IsSample { get; set; }

    public Dialect Dialect { get; set; }

    /// <summary>Setup do programa: bruto informado (null = comentário ou automático), fixações e ferramentas.</summary>
    public MachiningSetup Setup { get; set; } = MachiningSetup.CreateDefault("");

    /// <summary>Posição do cursor quando a aba saiu de foco.</summary>
    public int CaretOffset { get; set; }

    /// <summary>Chave do setup guardado: o caminho do arquivo (ou o nome, se não salvo).</summary>
    public string SetupKey => FilePath ?? DisplayName;

    /// <summary>Aba vazia e intocada, ou exemplo sem alterações: pode ser substituída ao abrir outro arquivo.</summary>
    public bool IsDisposable => !IsDirty && (IsSample || (FilePath is null && Text.TextLength == 0));
}
