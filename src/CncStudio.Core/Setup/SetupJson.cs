using System.Text.Json;
using System.Text.Json.Serialization;

namespace CncStudio.Core.Setup;

/// <summary>Resultado da leitura de um setup salvo.</summary>
/// <param name="Setup">O setup lido (já migrado para a versão atual), ou null se não deu para ler.</param>
/// <param name="Warning">Aviso para o usuário quando o setup salvo não pôde ser usado.</param>
/// <param name="Migrated">True quando veio de uma versão antiga e foi convertido.</param>
public sealed record SetupReadResult(MachiningSetup? Setup, string? Warning, bool Migrated = false);

/// <summary>
/// Leitura e gravação do setup em JSON (com geração de código: funciona também no navegador).
/// Lê a versão 1 (formato do protótipo web) e a converte para a atual; nunca lança exceção.
/// </summary>
public static class SetupJson
{
    public const string InvalidWarning = "O setup salvo está corrompido ou incompleto; foi usado o padrão.";
    public const string FutureWarning = "O setup salvo é de uma versão mais nova do Casillas Code; foi usado o padrão.";

    public static string Serialize(MachiningSetup setup) =>
        JsonSerializer.Serialize(setup, SetupJsonContext.Default.MachiningSetup);

    /// <summary>Lê um setup. Texto vazio = sem setup salvo (sem aviso).</summary>
    public static SetupReadResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SetupReadResult(null, null);
        }

        try
        {
            int version = ReadVersion(json);
            if (version > MachiningSetup.CurrentSchemaVersion)
            {
                return new SetupReadResult(null, FutureWarning);
            }

            MachiningSetup? setup;
            bool migrated = false;
            if (version == 1)
            {
                SetupV1? old = JsonSerializer.Deserialize(json, SetupJsonContext.Default.SetupV1);
                setup = old is null ? null : SetupMigration.FromV1(old);
                migrated = true;
            }
            else if (version == MachiningSetup.CurrentSchemaVersion)
            {
                setup = JsonSerializer.Deserialize(json, SetupJsonContext.Default.MachiningSetup);
            }
            else
            {
                return new SetupReadResult(null, InvalidWarning);
            }

            return setup is not null && setup.IsValid()
                ? new SetupReadResult(setup, null, migrated)
                : new SetupReadResult(null, InvalidWarning);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return new SetupReadResult(null, InvalidWarning);
        }
    }

    /// <summary>Lê o setup salvo ou cria o padrão do programa (com o aviso, se o salvo não serviu).</summary>
    public static (MachiningSetup Setup, string? Warning) LoadOrDefault(string? json, string projectKey)
    {
        SetupReadResult read = Parse(json);
        if (read.Setup is { } setup)
        {
            setup.ProjectKey = projectKey ?? "";
            return (setup, null);
        }

        return (MachiningSetup.CreateDefault(projectKey), read.Warning);
    }

    /// <summary>SchemaVersion do JSON (0 se ausente ou não numérico).</summary>
    private static int ReadVersion(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("O setup precisa ser um objeto.");
        }

        return doc.RootElement.TryGetProperty("SchemaVersion", out JsonElement v) && v.TryGetInt32(out int n) ? n : 0;
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MachiningSetup))]
[JsonSerializable(typeof(SetupV1))]
internal partial class SetupJsonContext : JsonSerializerContext;
