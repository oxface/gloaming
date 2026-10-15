using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.Samples.EventReactions.Consumer.Tests;

// A test harness over two independently composed consumers, not a proposed runtime interface.
internal sealed class Scenario : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    internal Scenario(
        string shape,
        string connection,
        int ceiling = 128,
        bool asyncOnly = false,
        params IInterceptor[] interceptors
    )
    {
        IsDirect = shape == "direct";
        var services = new ServiceCollection();
        if (IsDirect)
        {
            services.AddDirectOrdersModule(
                connection,
                events =>
                {
                    events.MaxReactionInvocations = ceiling;
                    if (asyncOnly)
                        events.AddReaction<OrderSubmitted, AsyncOnlyObserver<OrderSubmitted>>();
                }
            );
            services.ConfigureDbContext<DirectOrdersContext>(
                (_, options) => options.AddInterceptors(interceptors)
            );
        }
        else
        {
            services.AddMappedQuotesModule(
                connection,
                events =>
                {
                    events.MaxReactionInvocations = ceiling;
                    if (asyncOnly)
                        events.AddReaction<QuoteAccepted, AsyncOnlyObserver<QuoteAccepted>>();
                }
            );
            services.ConfigureDbContext<MappedQuotesContext>(
                (_, options) => options.AddInterceptors(interceptors)
            );
        }

        _provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        _scope = _provider.CreateAsyncScope();
        Context = IsDirect
            ? _scope.ServiceProvider.GetRequiredService<DirectOrdersContext>()
            : _scope.ServiceProvider.GetRequiredService<MappedQuotesContext>();
    }

    internal DbContext Context { get; }
    internal bool IsDirect { get; }
    internal string Schema => IsDirect ? "direct_orders" : "mapped_quotes";
    internal int PendingCount =>
        (
            Context is DirectOrdersContext direct
                ? direct.Orders.Local.Sum(order => order.Events.Count)
                    + direct.Allowances.Local.Sum(allowance => allowance.Events.Count)
                : 0
        )
        + (
            IsDirect
                ? _scope
                    .ServiceProvider.GetRequiredService<
                        EventReactionDispatcher<DirectOrdersModule>
                    >()
                    .PendingCount
                : _scope
                    .ServiceProvider.GetRequiredService<
                        EventReactionDispatcher<MappedQuotesModule>
                    >()
                    .PendingCount
        );

    internal void RaiseMapped(object fact) =>
        _scope.ServiceProvider.GetRequiredService<IEventSink<MappedQuotesModule>>().Raise(fact);

    internal void Seed(Guid id, decimal amount, decimal allowance = 100)
    {
        if (Context is DirectOrdersContext direct)
        {
            direct.Allowances.Add(DirectAllowance.Open(direct.Owner, allowance));
            direct.Orders.Add(DirectOrder.Draft(direct.Owner, id, amount));
        }
        else
        {
            var mapped = (MappedQuotesContext)Context;
            mapped.Allowances.Add(
                new()
                {
                    Owner = mapped.Owner,
                    Ceiling = allowance,
                    Version = 1,
                }
            );
            AddDraft(id, amount);
        }
    }

    internal void AddDraft(Guid id, decimal amount)
    {
        if (Context is DirectOrdersContext direct)
            direct.Orders.Add(DirectOrder.Draft(direct.Owner, id, amount));
        else
        {
            var mapped = (MappedQuotesContext)Context;
            mapped.Quotes.Add(
                new()
                {
                    Owner = mapped.Owner,
                    Id = id,
                    Amount = amount,
                    Version = 1,
                }
            );
        }
    }

    internal async Task SubmitAsync(Guid id, CancellationToken token)
    {
        if (Context is DirectOrdersContext direct)
        {
            (await direct.Orders.SingleAsync(row => row.Id == id, token)).Submit();
        }
        else
            await _scope
                .ServiceProvider.GetRequiredService<MappedQuoteCommands>()
                .AcceptAsync(id, token);
    }

    internal async Task<object> DeleteAsync(Guid id, CancellationToken token)
    {
        if (Context is DirectOrdersContext direct)
        {
            var root = await direct.Orders.SingleAsync(row => row.Id == id, token);
            root.Delete();
            direct.Orders.Remove(root);
            return root;
        }

        var mapped = (MappedQuotesContext)Context;
        await _scope
            .ServiceProvider.GetRequiredService<MappedQuoteCommands>()
            .DeleteAsync(id, token);
        return mapped
            .ChangeTracker.Entries<QuoteRow>()
            .Single(entry => entry.Entity.Id == id)
            .Entity;
    }

    internal Task LoadAllowanceAsync(CancellationToken token) =>
        Context is DirectOrdersContext direct
            ? direct.Allowances.SingleAsync(token)
            : ((MappedQuotesContext)Context).Allowances.SingleAsync(token);

    internal void AcceptAllChanges() => Context.ChangeTracker.AcceptAllChanges();

    internal async Task<OrderState?> ReadAsync(Guid id, CancellationToken token) =>
        Context is DirectOrdersContext direct
            ? await direct
                .Orders.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new OrderState(row.Id, row.Amount, row.Submitted, row.Version))
                .SingleOrDefaultAsync(token)
            : await ((MappedQuotesContext)Context)
                .Quotes.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new OrderState(row.Id, row.Amount, row.Accepted, row.Version))
                .SingleOrDefaultAsync(token);

    internal async Task<AllowanceState> StateAsync(CancellationToken token)
    {
        if (Context is DirectOrdersContext direct)
        {
            var allowance = await direct.Allowances.AsNoTracking().SingleAsync(token);
            return new(
                allowance.Reserved,
                allowance.Version,
                await direct.Receipts.CountAsync(token),
                await direct.Allocations.CountAsync(token)
            );
        }

        var mapped = (MappedQuotesContext)Context;
        var saved = await mapped.Allowances.AsNoTracking().SingleAsync(token);
        return new(
            saved.Reserved,
            saved.Version,
            await mapped.Receipts.CountAsync(token),
            await mapped.Allocations.CountAsync(token)
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private sealed class AsyncOnlyObserver<TEvent> : IEventReaction<TEvent>
        where TEvent : notnull
    {
        public Task HandleAsync(TEvent fact, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
