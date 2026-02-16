using System.Collections.Concurrent;

namespace RhythmbulletPrototype.Editor;

public sealed class QueuedEditorView : IEditorView
{
    private readonly ConcurrentQueue<EditorCommand> _commands = new();
    private readonly ConcurrentQueue<string> _messages = new();

    public EditorViewModel? LastModel { get; private set; }

    public void Enqueue(EditorCommand command)
    {
        _commands.Enqueue(command);
    }

    public bool TryDequeueMessage(out string message)
    {
        return _messages.TryDequeue(out message!);
    }

    public void Render(EditorViewModel model)
    {
        LastModel = model;
    }

    public bool TryDequeueCommand(out EditorCommand command)
    {
        return _commands.TryDequeue(out command!);
    }

    public void PushMessage(string message)
    {
        _messages.Enqueue(message);
    }
}

