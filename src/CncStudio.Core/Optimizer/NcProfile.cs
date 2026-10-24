namespace CncStudio.Core.Optimizer;

/// <summary>How the control reads a dimension word written without a decimal point.</summary>
public enum DecimalInput
{
    /// <summary>Parameter 3401#0 = 1: X10 means 10 mm (or 10 inch in G20).</summary>
    Calculator,
    /// <summary>Parameter 3401#0 = 0: X10 means 10 least increments (0.010 mm in G21, IS-B).</summary>
    Conventional
}

/// <summary>Machine/control assumptions used by interpretation and Macro B generation.
/// A profile never authorizes machine execution.</summary>
public sealed record NcProfile(string Name, DecimalInput DecimalInput, int VariableBase,
    int MaxPeriod = 1000, int MinSavedLines = 3)
{
    /// <summary>FANUC 0i-MF of the user's machine: X10 = 10 mm (calculator-type input).</summary>
    public static NcProfile Fanuc0iMfCalculator { get; } =
        new("FANUC 0i-MF - entrada decimal tipo calculadora (3401#0=1)", DecimalInput.Calculator, 100);

    public static NcProfile Fanuc0iMfConventional { get; } =
        new("FANUC 0i-MF - entrada decimal convencional (3401#0=0)", DecimalInput.Conventional, 100);

    public int CounterVariable => VariableBase;
    public IReadOnlyList<int> Variables => new[] { VariableBase, VariableBase + 1, VariableBase + 2, VariableBase + 3 };

    /// <summary>Value of a dimension word in program units (mm in G21, inch in G20).</summary>
    public double Dimension(NcWord word, int units) =>
        word.HasDecimalPoint || DecimalInput == DecimalInput.Calculator
            ? word.Value
            : word.Value * (units == 20 ? 0.0001 : 0.001);
}
