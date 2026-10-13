using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rootbolt.Messaging;
using Rootbolt.Messaging.EntityFrameworkCore;
using Rootbolt.Messaging.Workflows;

namespace Rootbolt.Messaging.Workflows.EntityFrameworkCore;

/// <summary>Executes explicit continuations and tracks state/request changes in the owning native EF transaction.</summary>
public sealed class EfWorkflow<TDbContext, TStart, TState> : IWorkflow<TStart, TState>
    where TDbContext : DbContext
    where TStart : class
    where TState : class
{
    private readonly TDbContext database;
    private readonly IOutbox<TDbContext> outbox;
    private readonly WorkflowDefinition<TStart, TState> definition;
    private static readonly JsonSerializerOptions LedgerJson = new(JsonSerializerDefaults.Web);

    /// <summary>Bind the owning context, its typed outbox and immutable definition. No database I/O occurs here.</summary>
    public EfWorkflow(
        TDbContext database,
        IOutbox<TDbContext> outbox,
        WorkflowDefinition<TStart, TState> definition
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(definition);

        this.database = database;
        this.outbox = outbox;
        this.definition = definition;
    }

    /// <inheritdoc />
    public async Task<WorkflowSnapshot<TState>> StartAsync(
        WorkflowKey key,
        TStart input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var transaction = RequireWrite(key);
        var initialInput = JsonSerializer.SerializeToElement(input, definition.Json);
        var row = await Query(key).SingleOrDefaultAsync(cancellationToken);
        if (row is not null)
        {
            ValidateLoaded(row);
            if (!JsonElement.DeepEquals(row.InitialInput, initialInput))
                throw new InvalidOperationException(
                    "A workflow identity cannot be restarted with different input."
                );

            return Snapshot(row);
        }

        row = WorkflowInstanceRecord.Create(
            definition.WorkflowType,
            key,
            definition.DefinitionVersion,
            initialInput
        );
        var context = new WorkflowContext(key, Guid.NewGuid(), null);
        // Initial methods may create random business identities: invoke only after checking retained input.
        var turn = definition.Start(context, input);
        return Apply(row, context, turn, [], transaction, isNew: true);
    }

    /// <inheritdoc />
    public async Task<WorkflowSnapshot<TState>> HandleReplyAsync(
        WorkflowKey key,
        IncomingMessage reply,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(reply);

        var transaction = RequireWrite(key);
        if (
            reply.TenantKey != key.TenantKey
            || reply.CorrelationId != key.Id.ToString("D")
            || !Guid.TryParseExact(reply.CausationId, "D", out var requestedId)
            || requestedId == Guid.Empty
        )
            throw new InvalidDataException(
                "Reply tenant, correlation or causation does not match the workflow."
            );

        var row = await Load(key, cancellationToken);
        var operations = ReadOperations(row);
        var operationIndex = operations.FindIndex(operation => operation.CommandId == requestedId);
        if (operationIndex < 0)
            throw new InvalidDataException("Reply has no requested workflow operation.");

        var operation = operations[operationIndex];
        if (operation.Outcome is not null)
        {
            if (!operation.Outcome.Matches(reply))
                throw new InvalidDataException("Reply conflicts with a retained outcome.");

            return Snapshot(row);
        }

        if (
            row.Status != WorkflowStatus.Waiting
            || row.PendingCommandId != requestedId
            || row.ContinuationName != operation.ContinuationName
        )
            throw new InvalidDataException("Reply is not for the pending operation.");

        if (
            !definition.Replies.TryGetValue(operation.ContinuationName, out var binding)
            || binding.MessageName != reply.MessageName
            || binding.SchemaVersion != reply.SchemaVersion
        )
            throw new InvalidDataException("Reply does not match the bound wire contract.");

        var context = new WorkflowContext(key, Guid.NewGuid(), reply.MessageId);
        var candidate = Restore(row.State);
        var turn = binding.Invoke(context, candidate, reply.Payload);
        operations[operationIndex] = operation with
        {
            Outcome = new(reply.ProducerKey, reply.MessageName, reply.SchemaVersion, reply.Payload),
        };
        return Apply(row, context, turn, operations, transaction, isNew: false);
    }

    /// <inheritdoc />
    public async Task<WorkflowSnapshot<TState>> SignalAsync<TSignal>(
        WorkflowKey key,
        TSignal signal,
        CancellationToken cancellationToken = default
    )
        where TSignal : class
    {
        ArgumentNullException.ThrowIfNull(signal);

        var transaction = RequireWrite(key);
        var row = await Load(key, cancellationToken);
        if (
            row.Status != WorkflowStatus.AwaitingInput
            || row.ContinuationName is null
            || !definition.Signals.TryGetValue(row.ContinuationName, out var binding)
            || binding.SignalType != typeof(TSignal)
        )
            throw new InvalidOperationException(
                "This workflow is not waiting for the supplied signal."
            );

        var context = new WorkflowContext(key, Guid.NewGuid(), null);
        return Apply(
            row,
            context,
            binding.Invoke(context, Restore(row.State), signal),
            ReadOperations(row),
            transaction,
            isNew: false
        );
    }

    /// <inheritdoc />
    public async Task<WorkflowSnapshot<TState>?> ReadAsync(
        WorkflowKey key,
        CancellationToken cancellationToken = default
    )
    {
        ValidateKey(key);
        ValidateModel();

        var row = await Query(key).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        ValidateLoaded(row);
        return Snapshot(row);
    }

    private WorkflowSnapshot<TState> Apply(
        WorkflowInstanceRecord row,
        WorkflowContext context,
        WorkflowTurn<TState> turn,
        List<WorkflowOperation> operations,
        IDbContextTransaction transaction,
        bool isNew
    )
    {
        ArgumentNullException.ThrowIfNull(turn);

        if (turn.Status == WorkflowStatus.Waiting)
        {
            var command = turn.Command!;
            if (
                !definition.Replies.ContainsKey(turn.ContinuationName!)
                || command.MessageId != context.CommandId
                || command.TenantKey != context.Key.TenantKey
                || command.CorrelationId != context.Key.Id.ToString("D")
            )
                throw new InvalidOperationException(
                    "A request must retain execution identity/tenant/correlation and name a reply binding."
                );

            operations.Add(
                new(command.MessageId, turn.ContinuationName!, command.MessageName, null)
            );
        }
        else if (
            turn.Status == WorkflowStatus.AwaitingInput
            && !definition.Signals.ContainsKey(turn.ContinuationName!)
        )
            throw new InvalidOperationException(
                "An input pause must name a registered signal binding."
            );

        // Serialize and restore before touching tracked state. Bad serializers/required fields leave no proposal.
        var state = JsonSerializer.SerializeToElement(turn.State, definition.Json);
        var detachedState = Restore(state);
        var ledger = JsonSerializer.SerializeToElement(operations, LedgerJson);
        if (row.Version == long.MaxValue)
            throw new InvalidDataException("Workflow version is exhausted.");

        OutboxMessageRecord? commandRow = null;
        if (turn.Command is not null)
        {
            outbox.Enqueue(turn.Command);
            commandRow = database
                .ChangeTracker.Entries<OutboxMessageRecord>()
                .Single(entry =>
                    entry.State == EntityState.Added
                    && entry.Entity.MessageId == turn.Command.MessageId
                )
                .Entity;
        }

        row.Advance(turn.Status, turn.ContinuationName, turn.Command?.MessageId, state, ledger);
        if (isNew)
            database.Add(row);

        WorkflowChangeRegistry.Register(database, row, commandRow, transaction);
        return new(
            context.Key,
            row.DefinitionVersion,
            row.Version,
            row.Status,
            row.ContinuationName,
            row.PendingCommandId,
            detachedState
        );
    }

    private IDbContextTransaction RequireWrite(WorkflowKey key)
    {
        ValidateKey(key);
        ValidateModel();
        if (
            Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
        )
            throw new InvalidOperationException(
                "Workflow writes require an explicit native EF transaction, not an ambient transaction."
            );

        var transaction =
            database.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Workflow writes require an explicit native EF transaction."
            );
        database.ChangeTracker.DetectChanges();
        var tracked = database
            .ChangeTracker.Entries<WorkflowInstanceRecord>()
            .SingleOrDefault(entry =>
                entry.Entity.WorkflowType == definition.WorkflowType && entry.Entity.Id == key.Id
            );
        if (
            tracked is not null
            && (
                tracked.State != EntityState.Unchanged
                || tracked.Entity.TenantKey != key.TenantKey
                || WorkflowChangeRegistry
                    .For(database)
                    .Any(proposal => ReferenceEquals(proposal.Row, tracked.Entity))
            )
        )
            throw new InvalidOperationException(
                "Discard the scope after a proposal; a changed or reused workflow cannot execute another turn."
            );

        return transaction;
    }

    private IQueryable<WorkflowInstanceRecord> Query(WorkflowKey key) =>
        database
            .Set<WorkflowInstanceRecord>()
            .Where(row =>
                row.WorkflowType == definition.WorkflowType
                && row.Id == key.Id
                && row.TenantKey == key.TenantKey
            );

    private async Task<WorkflowInstanceRecord> Load(
        WorkflowKey key,
        CancellationToken cancellationToken
    )
    {
        var row =
            await Query(key).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidDataException("No workflow exists for this admitted key.");
        ValidateLoaded(row);
        return row;
    }

    private void ValidateLoaded(WorkflowInstanceRecord row)
    {
        if (
            row.DefinitionVersion != definition.DefinitionVersion
            || row.Version < 1
            || !Enum.IsDefined(row.Status)
            || (
                row.Status == WorkflowStatus.Waiting
                && (
                    row.PendingCommandId is null
                    || row.PendingCommandId == Guid.Empty
                    || row.ContinuationName is null
                )
            )
            || (row.Status != WorkflowStatus.Waiting && row.PendingCommandId is not null)
            || (row.Status == WorkflowStatus.Completed && row.ContinuationName is not null)
            || (row.Status == WorkflowStatus.AwaitingInput && row.ContinuationName is null)
        )
            throw new InvalidDataException(
                "Stored workflow lifecycle or definition version is incompatible."
            );
    }

    private static List<WorkflowOperation> ReadOperations(WorkflowInstanceRecord row) =>
        row.Operations.Deserialize<List<WorkflowOperation>>(LedgerJson)
        ?? throw new InvalidDataException("Workflow operation ledger must not be null.");

    private TState Restore(JsonElement state) =>
        state.Deserialize<TState>(definition.Json)
        ?? throw new JsonException("Workflow state must not be null.");

    private WorkflowSnapshot<TState> Snapshot(WorkflowInstanceRecord row) =>
        new(
            new(row.Id, row.TenantKey),
            row.DefinitionVersion,
            row.Version,
            row.Status,
            row.ContinuationName,
            row.PendingCommandId,
            Restore(row.State)
        );

    private static void ValidateKey(WorkflowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfEqual(key.Id, Guid.Empty);
        if (key.TenantKey is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(key.TenantKey);
    }

    private void ValidateModel()
    {
        var model = database.Model.FindEntityType(typeof(WorkflowInstanceRecord));
        if (
            model?.FindPrimaryKey() is not { } key
            || !key
                .Properties.Select(property => property.Name)
                .SequenceEqual(
                    new[]
                    {
                        nameof(WorkflowInstanceRecord.WorkflowType),
                        nameof(WorkflowInstanceRecord.Id),
                    }
                )
            || model.FindProperty(nameof(WorkflowInstanceRecord.Version))?.IsConcurrencyToken
                != true
            || model.FindProperty(nameof(WorkflowInstanceRecord.TenantKey))?.IsConcurrencyToken
                != true
        )
            throw new InvalidOperationException(
                "ConfigureWorkflows must register the provided workflow key and concurrency tokens."
            );
    }
}
