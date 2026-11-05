using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;

namespace CncStudio.Core.Academy;

/// <summary>Dialeto e máquina de uma aula ("fanuc"|"siemens", "fresa"|"torno") para ler e conferir os programas dela.</summary>
public static class AcademyTarget
{
    public static readonly IReadOnlyList<string> Dialects = ["fanuc", "siemens"];
    public static readonly IReadOnlyList<string> Machines = ["fresa", "torno"];

    public static Dialect DialectOf(string? name) => name == "siemens" ? Dialect.Siemens : Dialect.Fanuc;

    /// <summary>
    /// Fresa usa a configuração padrão (a mesma de antes do v2, para os programas do v1 lerem igual); torno força a máquina torno
    /// (X em diâmetro).
    /// </summary>
    public static MachineSettings SettingsOf(string? machine) =>
        machine == "torno" ? new MachineSettings { Machine = MachineKind.Lathe } : MachineSettings.Default;
}
