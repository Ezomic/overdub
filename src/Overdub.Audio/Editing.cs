namespace Overdub.Audio;

public enum EditKind
{
    Clips,
    Tracks,
    Mix,
}

public readonly record struct EditChange(EditKind Kind, bool Replayed);

public sealed record EditCommand(string Name, Action Do, Action Undo, EditKind Kind, string? MergeKey = null, long At = 0);

public sealed class EditHistory
{
    private const int Limit = 200;
    private const long MergeWindowMilliseconds = 1000;

    private readonly LinkedList<EditCommand> _undo = new();
    private readonly Stack<EditCommand> _redo = new();

    public event Action<EditChange>? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Last?.Value.Name;
    public string? RedoName => _redo.TryPeek(out var command) ? command.Name : null;

    public void Execute(string name, Action doIt, Action undo, EditKind kind = EditKind.Clips, string? mergeKey = null)
    {
        doIt();
        var now = Environment.TickCount64;
        if (mergeKey is not null && _undo.Last is { Value: var last } && last.MergeKey == mergeKey && now - last.At < MergeWindowMilliseconds)
        {
            _undo.Last.Value = last with { Do = doIt, At = now };
        }
        else
        {
            Push(new EditCommand(name, doIt, undo, kind, mergeKey, now));
        }

        _redo.Clear();
        Changed?.Invoke(new EditChange(kind, false));
    }

    public void Record(string name, Action doIt, Action undo, EditKind kind = EditKind.Clips)
    {
        Push(new EditCommand(name, doIt, undo, kind, null, Environment.TickCount64));
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Last is not { Value: var command } node)
        {
            return;
        }

        _undo.Remove(node);
        command.Undo();
        _redo.Push(command);
        Changed?.Invoke(new EditChange(command.Kind, true));
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var command))
        {
            return;
        }

        command.Do();
        _undo.AddLast(command with { At = 0 });
        Changed?.Invoke(new EditChange(command.Kind, true));
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void Push(EditCommand command)
    {
        _undo.AddLast(command);
        if (_undo.Count > Limit)
        {
            _undo.RemoveFirst();
        }
    }
}
