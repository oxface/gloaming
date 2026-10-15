namespace Gloaming.EventReactions;

public sealed class EventReactionRegistry<TModule>
{
    private readonly Dictionary<Type, List<Handler>> _handlers = [];
    private bool _frozen;

    public void Add<TEvent>(IEventReaction<TEvent> handler)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_frozen)
            throw new InvalidOperationException(
                "Registration is fixed before constructing a dispatcher."
            );

        if (!_handlers.TryGetValue(typeof(TEvent), out var bindings))
        {
            bindings = [];
            _handlers.Add(typeof(TEvent), bindings);
        }

        if (bindings.Any(binding => ReferenceEquals(binding.Instance, handler)))
            throw new InvalidOperationException(
                "The same handler instance is already registered for this event."
            );

        var sync = handler as ISynchronousEventReaction<TEvent>;
        bindings.Add(
            new Handler(
                handler,
                sync is null ? null : fact => sync.Handle((TEvent)fact),
                (fact, token) => handler.HandleAsync((TEvent)fact, token)
            )
        );
    }

    internal void Freeze() => _frozen = true;

    internal IReadOnlyList<Handler> For(object fact) =>
        _handlers.TryGetValue(fact.GetType(), out var bindings)
            ? bindings
            : throw new InvalidOperationException(
                $"No handler is registered for {fact.GetType().Name} in this module."
            );

    internal sealed record Handler(
        object Instance,
        Action<object>? Sync,
        Func<object, CancellationToken, Task> Async
    );
}
