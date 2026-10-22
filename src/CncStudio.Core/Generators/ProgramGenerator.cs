using System.Globalization;
using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>Como o programa gerado chama o ciclo: tudo calculado (G-code simples) ou G65 com a macro junto.</summary>
public enum GeneratorOutput
{
    /// <summary>Cada posição e passe escritos (G81/G83 ou G01); roda em qualquer controle Fanuc, sem macro instalada.</summary>
    Plain,

    /// <summary>Chamada G65 P90xx com os argumentos, seguida da macro no mesmo arquivo (precisa de macro B no controle).</summary>
    MacroCall,
}

/// <summary>Um campo do formulário do gerador.</summary>
/// <param name="Id">Identificador estável (é também a letra do G65 quando o campo vira argumento).</param>
/// <param name="Label">Texto do campo, em português.</param>
/// <param name="Unit">Unidade (mm, graus, rpm…); vazio se não tem.</param>
/// <param name="Default">Valor inicial do formulário.</param>
/// <param name="Min">Menor valor aceito.</param>
/// <param name="Max">Maior valor aceito.</param>
/// <param name="IsInteger">O valor precisa ser inteiro.</param>
/// <param name="Hint">Ajuda curta sob o campo.</param>
public sealed record GeneratorParameter(string Id, string Label, string Unit, double Default, double Min, double Max, bool IsInteger = false, string Hint = "");

/// <summary>Resultado de gerar: o texto do programa ou a lista de problemas dos valores informados.</summary>
public sealed record GeneratorResult(string Program, IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Gerador de programa a partir de um formulário. Cada gerador sabe validar os valores, escrever o programa
/// pronto (<see cref="GeneratorOutput.Plain"/>) e escrever a chamada G65 com a macro (<see cref="GeneratorOutput.MacroCall"/>).
/// A prévia 3D é o próprio programa gerado passando pelo analisador e pelo simulador do editor.
/// </summary>
public abstract class ProgramGenerator
{
    public abstract string Id { get; }

    public abstract string Title { get; }

    public abstract string Description { get; }

    public abstract IReadOnlyList<GeneratorParameter> Parameters { get; }

    /// <summary>Valores iniciais do formulário.</summary>
    public IReadOnlyDictionary<string, double> Defaults => Parameters.ToDictionary(p => p.Id, p => p.Default);

    /// <summary>Confere cada valor contra a faixa do campo e as regras do gerador; devolve as mensagens (vazio = ok).</summary>
    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, double> values)
    {
        List<string> errors = [];
        foreach (GeneratorParameter p in Parameters)
        {
            if (!values.TryGetValue(p.Id, out double v) || double.IsNaN(v) || double.IsInfinity(v))
            {
                errors.Add($"{p.Label}: informe um número.");
            }
            else if (v < p.Min || v > p.Max)
            {
                errors.Add($"{p.Label}: precisa estar entre {Fmt(p.Min)} e {Fmt(p.Max)}{Unit(p)}.");
            }
            else if (p.IsInteger && Math.Abs(v - Math.Round(v)) > 1e-9)
            {
                errors.Add($"{p.Label}: precisa ser um número inteiro.");
            }
        }

        if (errors.Count == 0)
        {
            errors.AddRange(ValidateRules(values));
        }

        return errors;
    }

    public GeneratorResult Generate(IReadOnlyDictionary<string, double> values, GeneratorOutput output)
    {
        IReadOnlyList<string> errors = Validate(values);
        return errors.Count > 0 ? new GeneratorResult("", errors) : new GeneratorResult(Build(values, output), []);
    }

    /// <summary>Regras entre campos (ex.: bolsão maior que a fresa); os campos já passaram pela faixa.</summary>
    protected abstract IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> values);

    protected abstract string Build(IReadOnlyDictionary<string, double> values, GeneratorOutput output);

    /// <summary>Texto da macro B (mesmo arquivo de samples/macros/geradores, embutido no Core).</summary>
    protected static string MacroText(string fileName)
    {
        var assembly = typeof(ProgramGenerator).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("Macro." + fileName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Macro embutida não encontrada: {fileName}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n").Trim();
    }

    /// <summary>Número no formato Fanuc, com ponto decimal (50.), até 3 casas e sem zeros à direita.</summary>
    protected static string Word(char letter, double value) => letter + Num(value);

    protected static string Num(double value)
    {
        double rounded = Math.Round(value, 3);
        if (rounded == 0)
        {
            rounded = 0; // evita "-0."
        }

        string s = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        return s.Contains('.') ? s : s + ".";
    }

    /// <summary>Número para mensagens (sem o ponto final do Fanuc).</summary>
    protected static string Fmt(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));

    private static string Unit(GeneratorParameter p) => p.Unit.Length == 0 ? "" : " " + p.Unit;

    /// <summary>Abre e fecha o arquivo: cabeçalho comum de um programa Fanuc de fresa.</summary>
    protected static void Header(StringBuilder sb, string title, int program, int tool, double speed, string toolNote)
    {
        sb.Append('%').Append('\n');
        sb.Append('O').Append(program.ToString(CultureInfo.InvariantCulture)).Append(" (").Append(title).Append(")\n");
        sb.Append("(RASCUNHO GERADO PELO CASILLAS CODE - CONFIRA ANTES DE USAR NA MAQUINA)\n");
        sb.Append("G21 G17 G40 G49 G80 G90 G94\n");
        sb.Append("G54\n");
        sb.Append('T').Append(tool.ToString(CultureInfo.InvariantCulture)).Append(" M06 (").Append(toolNote).Append(")\n");
        sb.Append("G43 H").Append(tool.ToString(CultureInfo.InvariantCulture)).Append(" Z50.\n");
        sb.Append(Word('S', speed).TrimEnd('.')).Append(" M03\n");
    }

    protected static void Footer(StringBuilder sb)
    {
        sb.Append("G00 Z50.\n");
        sb.Append("M05\n");
        sb.Append("M30\n");
    }
}
