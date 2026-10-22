using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.EventReactions.EntityFrameworkCore;

/// <summary>Explicit context/module reaction bindings and selected tracked or scoped event sources.</summary>
public sealed class EventReactionRegistration<TContext, TModule>
    where TContext : DbContext
{
    private readonly IServiceCollection _services;
    private readonly List<Action<EventReactionRegistry<TModule>, IServiceProvider>> _bindings = [];
    private Func<IServiceProvider, TContext, IEnumerable<PendingEvents>>? _collect;

    internal EventReactionRegistration(IServiceCollection services) => _services = services;

    /// <summary>Positive maximum reaction invocations across all waves of one save dispatch.</summary>
    public int MaxReactionInvocations { get; set; } = 128;

    /// <summary>Adds one exact-type reaction to this context/module before bindings are frozen; consumer registration order is retained.</summary>
    public EventReactionRegistration<TContext, TModule> AddReaction<TEvent, THandler>()
        where TEvent : notnull
        where THandler : class, IEventReaction<TEvent>
    {
        _services.TryAddScoped<THandler>();
        return AddReaction<TEvent, THandler>(provider => provider.GetRequiredService<THandler>());
    }

    /// <summary>Adds one exact-type reaction to this context/module before bindings are frozen; consumer registration order is retained.</summary>
    public EventReactionRegistration<TContext, TModule> AddReaction<TEvent, THandler>(
        Func<IServiceProvider, THandler> factory
    )
        where TEvent : notnull
        where THandler : class, IEventReaction<TEvent>
    {
        ArgumentNullException.ThrowIfNull(factory);

        _bindings.Add(
            (registry, provider) =>
            {
                var handler = new Lazy<THandler>(() => factory(provider));
                if (typeof(ISynchronousEventReaction<TEvent>).IsAssignableFrom(typeof(THandler)))
                    registry.Add<TEvent>(new DeferredSyncHandler<TEvent, THandler>(handler));
                else
                    registry.Add<TEvent>(new DeferredHandler<TEvent, THandler>(handler));
            }
        );
        return this;
    }

    /// <summary>Selects additional scoped event buffers explicitly for each save; no source discovery is inferred.</summary>
    public EventReactionRegistration<TContext, TModule> CollectSources(
        Func<IServiceProvider, TContext, IEnumerable<PendingEvents>> collect
    )
    {
        ArgumentNullException.ThrowIfNull(collect);

        var previous = _collect;
        _collect = previous is null
            ? collect
            : (provider, context) => previous(provider, context).Concat(collect(provider, context));
        return this;
    }

    /// <summary>Selects a PendingEvents buffer on tracked entities assignable to <typeparamref name="TEntity"/>.</summary>
    public EventReactionRegistration<TContext, TModule> CollectFrom<TEntity>(
        Func<TEntity, PendingEvents> selectEvents
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(selectEvents);

        return CollectSources(
            (_, context) =>
                context
                    .ChangeTracker.Entries<TEntity>()
                    .Select(entry => selectEvents(entry.Entity))
                    .ToArray()
        );
    }

    internal EventReactionDispatcher<TModule> CreateDispatcher(IServiceProvider provider)
    {
        var registry = new EventReactionRegistry<TModule>();
        foreach (var bind in _bindings)
            bind(registry, provider);

        return new(registry, new PendingEvents(), MaxReactionInvocations);
    }

    internal IEnumerable<PendingEvents> Collect(IServiceProvider provider, TContext context) =>
        _collect?.Invoke(provider, context) ?? [];

    private class DeferredHandler<TEvent, THandler>(Lazy<THandler> handler) : IEventReaction<TEvent>
        where TEvent : notnull
        where THandler : IEventReaction<TEvent>
    {
        protected THandler Instance => handler.Value;

        public Task HandleAsync(TEvent fact, CancellationToken cancellationToken) =>
            Instance.HandleAsync(fact, cancellationToken);
    }

    private sealed class DeferredSyncHandler<TEvent, THandler>(Lazy<THandler> handler)
        : DeferredHandler<TEvent, THandler>(handler),
            ISynchronousEventReaction<TEvent>
        where TEvent : notnull
        where THandler : IEventReaction<TEvent>
    {
        public void Handle(TEvent fact) =>
            ((ISynchronousEventReaction<TEvent>)Instance).Handle(fact);
    }
}
