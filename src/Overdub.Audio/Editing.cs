namespace Overdub.Audio;

public sealed record EditCommand(string Name, Action Do, Action Undo);

public sealed class EditHistory
{
    private readonly Stack<EditCommand> _undo = new();
    private readonly Stack<EditCommand> _redo = new();

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.TryPeek(out var command) ? command.Name : null;
    public string? RedoName => _redo.TryPeek(out var command) ? command.Name : null;

    public void Execute(string name, Action doIt, Action undo)
    {
        doIt();
        _undo.Push(new EditCommand(name, doIt, undo));
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!_undo.TryPop(out var command))
        {
            return;
        }

        command.Undo();
        _redo.Push(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var command))
        {
            return;
        }

        command.Do();
        _undo.Push(command);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
