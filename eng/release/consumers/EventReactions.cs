using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
var supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("A disposable PostgreSQL server is required.");

string name = "gloaming_reactions_" + Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync(token);

try
{
    settings.Database = name;
    var services = new ServiceCollection();
    services.AddScoped<Operation>();
    // Register validation first: preparation must still complete every downstream wave first.
    services.TryAddGloamingCapability<OrdersContext>(
        "package-order-candidate",
        CapabilityPhase.Validate,
        (provider, database) => Validate(database, provider.GetRequiredService<Operation>())
    );
    services.AddEventReactions<OrdersContext, OrdersTag>(reactions =>
    {
        reactions.MaxReactionInvocations = 5;
        reactions.CollectFrom<Order>(order => order.Events);
        reactions.CollectSources(
            (provider, _) => [provider.GetRequiredService<Operation>().Events]
        );
        reactions.AddReaction<Submitted, StageReceipt>();
        reactions.AddReaction<Submitted, RecordSubmission>();
        reactions.AddReaction<Allocated, StageAllocation>();
        reactions.AddReaction<NoteReceived, StageNote>();
        reactions.AddReaction<LoopTick, RepeatTick>();
        reactions.AddReaction<AsyncJob, StageAsyncNote>();
    });
    services.AddDbContext<OrdersContext>(
        (provider, options) =>
            options
                .UseNpgsql(settings.ConnectionString)
                .ConfigureGloamingCapabilities<OrdersContext>(provider)
    );
    await using var provider = services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );
    await using (var setup = provider.CreateAsyncScope())
        await setup
            .ServiceProvider.GetRequiredService<OrdersContext>()
            .Database.EnsureCreatedAsync(token);

    foreach (bool synchronous in new[] { true, false })
    {
        Guid id = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var order = Submit(scope.ServiceProvider, id);
            await AssertAbsent(id);
            if (synchronous)
                database.SaveChanges(false);
            else
                await database.SaveChangesAsync(false, token);

            var operation = scope.ServiceProvider.GetRequiredService<Operation>();
            Check(
                operation.Trace.SequenceEqual(CompletedTrace()),
                "Capture/handler/wave order must precede validation."
            );
            Check(
                order.Events.Count == 0
                    && operation.Events.Count == 0
                    && scope
                        .ServiceProvider.GetRequiredService<EventReactionDispatcher<OrdersTag>>()
                        .PendingCount == 0,
                "Every explicitly selected source and the operation sink must drain."
            );
            database.ChangeTracker.AcceptAllChanges();
            operation.Trace.Clear();
            Check(
                database.SaveChanges() == 0 && await database.SaveChangesAsync(token) == 0,
                "Accepted repeated saves must not repeat reaction effects."
            );
            Check(
                operation.Trace.SequenceEqual(new[] { "validate", "validate" }),
                "Only validation repeats without new events."
            );
            await AssertAbsent(id);
            await transaction.CommitAsync(token);
        }

        await AssertCommitted(id);
    }

    Guid rolledBack = Guid.NewGuid();
    await using (var scope = provider.CreateAsyncScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var order = Submit(scope.ServiceProvider, rolledBack);
        await database.SaveChangesAsync(token);
        Check(order.Events.Count == 0, "Saving consumes captured facts before caller completion.");
        await transaction.RollbackAsync(token);
        Check(order.Events.Count == 0, "Rollback does not restore consumed in-memory facts.");
    }

    await AssertAbsent(rolledBack);
    await Recover(rolledBack);

    foreach (Failure failure in new[] { Failure.Handler, Failure.Candidate, Failure.Sql })
    {
        Guid id = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var operation = scope.ServiceProvider.GetRequiredService<Operation>();
            operation.Failure = failure;
            Submit(scope.ServiceProvider, id);
            if (failure == Failure.Handler)
                await ExpectAsync<HandlerRejectedException>(() => database.SaveChangesAsync(token));
            else if (failure == Failure.Candidate)
                await ExpectAsync<CandidateRejectedException>(() =>
                    database.SaveChangesAsync(token)
                );
            else
            {
                var exception = await ExpectAsync<DbUpdateException>(() =>
                    database.SaveChangesAsync(token)
                );
                Check(
                    exception.InnerException
                        is PostgresException { SqlState: PostgresErrorCodes.NotNullViolation },
                    "The native failure must be the deliberately invalid note column."
                );
            }

            if (failure != Failure.Handler)
                Check(
                    operation.Trace.SequenceEqual(CompletedTrace()),
                    "All preparation must precede validation and native SQL."
                );

            if (failure != Failure.Sql)
            {
                int before = operation.Trace.Count;
                await ExpectAsync<InvalidOperationException>(() =>
                    database.SaveChangesAsync(token)
                );
                Check(
                    operation.Trace.Count == before,
                    "Observed capability failure must reject scope reuse before handling."
                );
            }

            await transaction.RollbackAsync(token);
        }

        await AssertAbsent(id);
        await Recover(id);
    }

    Guid unbounded = Guid.NewGuid();
    await using (var scope = provider.CreateAsyncScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        database.Add(Order.Draft(unbounded));
        scope.ServiceProvider.GetRequiredService<IEventSink<OrdersTag>>().Raise(new LoopTick());
        await ExpectAsync<InvalidOperationException>(() => database.SaveChangesAsync(token));
        Check(
            scope.ServiceProvider.GetRequiredService<Operation>().LoopCalls == 5,
            "The configured invocation ceiling must stop the loop."
        );
    }

    await AssertAbsent(unbounded);
    await Recover(unbounded);

    Guid noTransaction = Guid.NewGuid();
    await using (var scope = provider.CreateAsyncScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
        Submit(scope.ServiceProvider, noTransaction);
        await ExpectAsync<InvalidOperationException>(() => database.SaveChangesAsync(token));
        Check(
            scope.ServiceProvider.GetRequiredService<Operation>().Trace.Count == 0,
            "Pending facts must require the caller transaction before handling."
        );
    }

    await AssertAbsent(noTransaction);
    await Recover(noTransaction);

    Guid asyncOnly = Guid.NewGuid();
    foreach (bool synchronous in new[] { true, false })
    {
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var operation = scope.ServiceProvider.GetRequiredService<Operation>();
        scope
            .ServiceProvider.GetRequiredService<IEventSink<OrdersTag>>()
            .Raise(new AsyncJob(asyncOnly));
        if (synchronous)
        {
            await ExpectAsync<InvalidOperationException>(() =>
            {
                database.SaveChanges();
                return Task.CompletedTask;
            });
            Check(
                operation.Trace.Count == 0,
                "Synchronous saving must reject rather than execute async-only work."
            );
            await AssertAbsent(asyncOnly);
        }
        else
        {
            await database.SaveChangesAsync(token);
            Check(
                operation.Trace.SequenceEqual(new[] { "async", "validate" }),
                "Asynchronous saving must await the reaction before validation."
            );
            await transaction.CommitAsync(token);
        }
    }

    await using (var observer = provider.CreateAsyncScope())
    {
        var database = observer.ServiceProvider.GetRequiredService<OrdersContext>();
        Check(
            await database
                .Set<Note>()
                .AsNoTracking()
                .Where(row => row.SubjectId == asyncOnly)
                .Select(row => row.Text)
                .SingleAsync(token) == "async",
            "The fresh async operation must persist its reaction result."
        );
    }

    Console.WriteLine(
        "PASS event reactions: package-only bounded waves, preparation/validation, native commit/rollback, fault recovery and sync/async lanes"
    );

    Order Submit(IServiceProvider scoped, Guid id)
    {
        var order = Order.Draft(id);
        scoped.GetRequiredService<OrdersContext>().Add(order);
        order.Submit();
        scoped.GetRequiredService<Operation>().Events.Raise(new NoteReceived(id, "request"));
        scoped.GetRequiredService<IEventSink<OrdersTag>>().Raise(new NoteReceived(id, "sink"));
        return order;
    }

    async Task Recover(Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OrdersContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        Submit(scope.ServiceProvider, id);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        await AssertCommitted(id);
    }

    async Task AssertAbsent(Guid id)
    {
        await using var observer = provider.CreateAsyncScope();
        var database = observer.ServiceProvider.GetRequiredService<OrdersContext>();
        Check(
            !await database.Set<Order>().AnyAsync(row => row.Id == id, token)
                && !await database.Set<Receipt>().AnyAsync(row => row.OrderId == id, token)
                && !await database.Set<Allocation>().AnyAsync(row => row.OrderId == id, token)
                && !await database.Set<Note>().AnyAsync(row => row.SubjectId == id, token),
            "No producer or reaction rows may survive an absent operation."
        );
    }

    async Task AssertCommitted(Guid id)
    {
        await using var observer = provider.CreateAsyncScope();
        var database = observer.ServiceProvider.GetRequiredService<OrdersContext>();
        Check(
            (
                await database.Set<Order>().AsNoTracking().SingleAsync(row => row.Id == id, token)
            ).Submitted,
            "The producer write must commit with its reactions."
        );
        Check(
            await database.Set<Receipt>().CountAsync(row => row.OrderId == id, token) == 1
                && await database.Set<Allocation>().CountAsync(row => row.OrderId == id, token)
                    == 1,
            "All downstream waves must commit once with the producer."
        );
        var notes = await database
            .Set<Note>()
            .Where(row => row.SubjectId == id)
            .OrderBy(row => row.Text)
            .Select(row => row.Text)
            .ToArrayAsync(token);
        Check(
            notes.SequenceEqual(new[] { "request", "sink" }),
            "Scoped sources and the operation sink must both persist once."
        );
    }
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync(CancellationToken.None);
}

static string[] CompletedTrace() =>
    ["receipt", "submitted", "note-request", "note-sink", "allocated", "validate"];

static void Validate(OrdersContext database, Operation operation)
{
    operation.Trace.Add("validate");
    foreach (var order in database.Set<Order>().Local.Where(row => row.Submitted))
    {
        Check(
            database.Set<Receipt>().Local.Any(row => row.OrderId == order.Id)
                && database.Set<Allocation>().Local.Any(row => row.OrderId == order.Id),
            "Validation must see all downstream candidate rows."
        );
    }

    if (operation.Failure == Failure.Candidate)
        throw new CandidateRejectedException();
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task<TException> ExpectAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class OrdersTag;

sealed class HandlerRejectedException : Exception;

sealed class CandidateRejectedException : Exception;

enum Failure
{
    None = 1,
    Handler = 2,
    Candidate = 3,
    Sql = 4,
}

sealed record Submitted(Guid Id);

sealed record Allocated(Guid Id);

readonly record struct NoteReceived(Guid Id, string Text);

sealed record LoopTick;

sealed record AsyncJob(Guid Id);

sealed class Operation
{
    internal PendingEvents Events { get; } = new();
    internal List<string> Trace { get; } = [];
    internal Failure Failure { get; set; } = Failure.None;
    internal int LoopCalls { get; set; }
}

sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("package_orders");
        model.ConfigureEventReactions();
        model.RequireGloamingCapability("package-order-candidate");
        model.Entity<Order>().HasKey(row => row.Id);
        model.Entity<Order>().Property(row => row.Id).ValueGeneratedNever();
        model.Entity<Order>().Ignore(row => row.Events);
        model.Entity<Receipt>().HasKey(row => row.OrderId);
        model.Entity<Receipt>().HasOne<Order>().WithMany().HasForeignKey(row => row.OrderId);
        model.Entity<Allocation>().HasKey(row => row.OrderId);
        model.Entity<Allocation>().HasOne<Order>().WithMany().HasForeignKey(row => row.OrderId);
        model.Entity<Note>().HasKey(row => row.Id);
        model.Entity<Note>().Property(row => row.Text).IsRequired();
    }
}

sealed class Order
{
    public Guid Id { get; private set; }
    public bool Submitted { get; private set; }
    public PendingEvents Events { get; } = new();

    internal static Order Draft(Guid id) => new() { Id = id };

    internal void Submit()
    {
        Submitted = true;
        Events.Raise(new Submitted(Id));
    }
}

sealed class Receipt
{
    public Guid OrderId { get; set; }
}

sealed class Allocation
{
    public Guid OrderId { get; set; }
}

sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubjectId { get; set; }
    public string Text { get; set; } = null!;
}

sealed class StageReceipt(OrdersContext database, IEventSink<OrdersTag> sink, Operation operation)
    : IEventReaction<Submitted>,
        ISynchronousEventReaction<Submitted>
{
    public void Handle(Submitted fact)
    {
        operation.Trace.Add("receipt");
        database.Add(new Receipt { OrderId = fact.Id });
        sink.Raise(new Allocated(fact.Id));
        if (operation.Failure == Failure.Handler)
            throw new HandlerRejectedException();
    }

    public Task HandleAsync(Submitted fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

sealed class RecordSubmission(Operation operation)
    : IEventReaction<Submitted>,
        ISynchronousEventReaction<Submitted>
{
    public void Handle(Submitted fact) => operation.Trace.Add("submitted");

    public Task HandleAsync(Submitted fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

sealed class StageAllocation(OrdersContext database, Operation operation)
    : IEventReaction<Allocated>,
        ISynchronousEventReaction<Allocated>
{
    public void Handle(Allocated fact)
    {
        operation.Trace.Add("allocated");
        database.Add(new Allocation { OrderId = fact.Id });
    }

    public Task HandleAsync(Allocated fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

sealed class StageNote(OrdersContext database, Operation operation)
    : IEventReaction<NoteReceived>,
        ISynchronousEventReaction<NoteReceived>
{
    public void Handle(NoteReceived fact)
    {
        operation.Trace.Add("note-" + fact.Text);
        database.Add(
            new Note
            {
                SubjectId = fact.Id,
                Text = operation.Failure == Failure.Sql ? null! : fact.Text,
            }
        );
    }

    public Task HandleAsync(NoteReceived fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

sealed class RepeatTick(IEventSink<OrdersTag> sink, Operation operation)
    : IEventReaction<LoopTick>,
        ISynchronousEventReaction<LoopTick>
{
    public void Handle(LoopTick fact)
    {
        operation.LoopCalls++;
        sink.Raise(new LoopTick());
    }

    public Task HandleAsync(LoopTick fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

sealed class StageAsyncNote(OrdersContext database, Operation operation) : IEventReaction<AsyncJob>
{
    public async Task HandleAsync(AsyncJob fact, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        operation.Trace.Add("async");
        database.Add(new Note { SubjectId = fact.Id, Text = "async" });
    }
}
