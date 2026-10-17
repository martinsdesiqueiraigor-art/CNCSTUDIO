using System.Text.Json.Serialization;

namespace CncStudio.Web;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MachiningSetup))]
internal partial class SetupJsonContext : JsonSerializerContext;
