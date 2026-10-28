namespace CncStudio.Core.Cam;

/// <summary>
/// Pilha de desfazer simples para listas de operações imutáveis: guarda cópias da lista antes de cada alteração.
/// Passa do limite? Descarta as mais antigas.
/// </summary>
public sealed class UndoStack<T>(int limit = 50)
{
    private readonly List<IReadOnlyList<T>> _snapshots = [];

    public bool CanUndo => _snapshots.Count > 0;

    public int Count => _snapshots.Count;

    /// <summary>Guarda uma cópia do estado atual (chamar antes de mudar).</summary>
    public void Push(IEnumerable<T> current)
    {
        _snapshots.Add(current.ToList());
        if (_snapshots.Count > limit)
        {
            _snapshots.RemoveAt(0);
        }
    }

    /// <summary>Tira o último estado guardado, ou null se não há nada a desfazer.</summary>
    public IReadOnlyList<T>? Pop()
    {
        if (_snapshots.Count == 0)
        {
            return null;
        }

        IReadOnlyList<T> last = _snapshots[^1];
        _snapshots.RemoveAt(_snapshots.Count - 1);
        return last;
    }

    public void Clear() => _snapshots.Clear();
}
