using System.Globalization;

namespace CncStudio.Core.Execution.Expressions;

/// <summary>
/// Variáveis do programa.
/// <list type="bullet">
/// <item>Fanuc: #1–#33 são locais (uma cópia por nível de chamada G65), #100–#999 são comuns
/// (globais), #0 é sempre vazia, #1000 em diante são de sistema (via <see cref="SystemVariables"/>).</item>
/// <item>Siemens: parâmetros R e variáveis DEF são globais; nomes com "$" são de sistema.</item>
/// </list>
/// </summary>
public sealed class VariableStore : IVariableScope
{
    private readonly Dictionary<string, double?> _globals = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<int, double?>> _locals = new();

    public VariableStore()
    {
        _locals.Push([]);
    }

    /// <summary>Leitura de variáveis de sistema (posição atual etc.). Null = não suportada.</summary>
    public Func<string, double?>? SystemVariables { get; set; }

    /// <summary>Nível de chamada de macro (0 = programa principal).</summary>
    public int Depth => _locals.Count - 1;

    public double? Get(string name)
    {
        if (TryFanucNumber(name, out int number))
        {
            if (number == 0)
            {
                return null;
            }

            if (number <= 33)
            {
                return _locals.Peek().TryGetValue(number, out double? local) ? local : null;
            }

            if (number >= 1000)
            {
                return SystemVariables?.Invoke(name);
            }
        }
        else if (name.StartsWith('$'))
        {
            return SystemVariables?.Invoke(name);
        }

        return _globals.TryGetValue(name, out double? value) ? value : null;
    }

    public void Set(string name, double? value)
    {
        if (TryFanucNumber(name, out int number))
        {
            if (number == 0)
            {
                throw new ExpressionException("A variável #0 é sempre vazia e não pode receber valor.");
            }

            if (number <= 33)
            {
                _locals.Peek()[number] = value;
                return;
            }

            if (number >= 1000)
            {
                throw new ExpressionException($"Variável de sistema {name} não pode ser alterada aqui.");
            }
        }

        _globals[name] = value;
    }

    /// <summary>Nova camada de variáveis locais (#1–#33) para uma chamada G65/G66.</summary>
    public void PushLocals(IReadOnlyDictionary<int, double>? arguments = null)
    {
        var frame = new Dictionary<int, double?>();
        if (arguments is not null)
        {
            foreach ((int key, double value) in arguments)
            {
                frame[key] = value;
            }
        }

        _locals.Push(frame);
    }

    public void PopLocals()
    {
        if (_locals.Count > 1)
        {
            _locals.Pop();
        }
    }

    private static bool TryFanucNumber(string name, out int number)
    {
        number = 0;
        return name.Length > 1 && name[0] == '#'
            && int.TryParse(name.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }
}
