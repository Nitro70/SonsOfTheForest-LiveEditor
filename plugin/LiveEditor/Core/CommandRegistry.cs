using System.Collections.Concurrent;

namespace LiveEditor.Core;

public sealed class CommandRegistry
{
    private readonly ConcurrentDictionary<string, ICommandHandler> _handlers = new();

    public void Register(ICommandHandler handler) => _handlers[handler.Name] = handler;

    public bool TryGet(string name, out ICommandHandler handler) =>
        _handlers.TryGetValue(name, out handler!);

    public IEnumerable<ICommandHandler> All => _handlers.Values;
}
