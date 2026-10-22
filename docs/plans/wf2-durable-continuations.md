# WF2 replacement proposal: bounded durable continuations

Status: the owner approved the public interface and exact replacement scope on 2026-10-12.
Implemented for line-by-line code review on `feat/compensating-workflow`; see the
[fresh slice report](../reports/wf2-durable-continuations.md). The original proposal is retained
below as the reviewed scope.

Naming refinement approved on 2026-10-13: HandleReplyAsync, SignalAsync, OnSignal,
WorkflowTurn.WaitForInput and WorkflowStatus.AwaitingInput. Examples and signatures below
use these final names; persisted numeric status values and continuation names are unchanged.

This replaces the uncommitted sequential saga experiment after review; it does not add a
second workflow runtime alongside it. The staged review snapshot remains untouched.

## What earns extraction

The active Sales reservation consumer currently owns database lookup, requested command IDs,
full reply validation, technical outcome recording, next-command selection, enqueueing and
transaction composition. Its fixed SequentialSaga also interprets a plan and reverses steps.
The owner instead wants ordinary continuation methods with stored progress and automatic
request enqueueing. The library must execute and resume those methods, not only store state.

The bounded protocol is: load one process, match its pending operation, invoke the configured
continuation once for a new result, and track the resulting process changes and outgoing
request in the existing owning EF transaction. The existing inbox processor saves and
commits replies; direct starts and operator resumption keep explicit consumer saves/commits.

The archived fulfilment process is historical evidence for requested identities, retained
results and operator recovery. It does not authorize importing cancellation, replenishment,
parallel lines or an archived runtime. Existing archive files and fixtures remain unchanged.

## Two packages, one capability

- `Gloaming.Messaging.Workflows` replaces the uncommitted `Gloaming.Messaging.Sagas` core.
  It depends only on existing `Gloaming.Messaging` and BCL System.Text.Json. It supplies
  immutable definitions, typed bindings, return instructions and execution metadata.
- `Gloaming.Messaging.Workflows.EntityFrameworkCore` replaces the uncommitted saga mapping
  adapter. It depends on the workflow core, existing `Gloaming.Messaging.EntityFrameworkCore`
  and EF Core Relational. It supplies a provided process row, model/save validation and a
  typed runner that uses the owning context and its existing typed outbox.

Neither existing Messaging package gains a dependency on Workflows. No PostgreSQL runtime
package, broker, event sourcing, Events codec, identity, tenancy or sample Contract dependency
is introduced. PostgreSQL is the first proved database; portable API signatures alone do not
establish another provider's JSON, transaction or concurrency behavior.

## Consumer code for review

Business state is ordinary consumer data, not a mapped aggregate or library-owned phase enum.
ReservationState retains the original lines, generated business reservation IDs, public
business status and refusal information. Those IDs are created only in the first Start call;
an identical repeated start does not invoke Start again.

```csharp
internal static WorkflowTurn<ReservationState> Start(
    WorkflowContext context,
    StartStockReservationOrder input)
{
    var state = ReservationState.Create(input);

    return WorkflowTurn<ReservationState>.Request(
        state,
        ReservationMessages.Reserve(context, state.First),
        continueAt: "first-reserved");
}

internal static WorkflowTurn<ReservationState> FirstReserved(
    WorkflowContext context,
    ReservationState state,
    StockReservationDecidedV1 reply)
{
    state.ValidateReservationReply(state.First, reply);

    if (reply.Decision != StockReservationDecision.Reserved)
        return WorkflowTurn<ReservationState>.Complete(state.Declined(reply));

    return WorkflowTurn<ReservationState>.Request(
        state.AwaitingSecond(),
        ReservationMessages.Reserve(context, state.Second),
        continueAt: "second-reserved");
}

internal static WorkflowTurn<ReservationState> SecondReserved(
    WorkflowContext context,
    ReservationState state,
    StockReservationDecidedV1 reply)
{
    state.ValidateReservationReply(state.Second, reply);

    if (reply.Decision == StockReservationDecision.Reserved)
        return WorkflowTurn<ReservationState>.Complete(state.Reserved());

    return WorkflowTurn<ReservationState>.Request(
        state.Releasing(reply),
        ReservationMessages.Release(context, state.First.ReservationId),
        continueAt: "first-released");
}

internal static WorkflowTurn<ReservationState> FirstReleased(
    WorkflowContext context,
    ReservationState state,
    StockReservationReleasedV1 reply)
{
    state.ValidateReleaseReply(state.First, reply);

    return reply.Decision == StockReservationReleaseDecision.Released
        ? WorkflowTurn<ReservationState>.Complete(state.Compensated())
        : WorkflowTurn<ReservationState>.WaitForInput(
            state.ReleaseRefused(reply), continueAt: "retry-release");
}

internal static WorkflowTurn<ReservationState> RetryRelease(
    WorkflowContext context,
    ReservationState state,
    RetryReservationRelease signal)
{
    return WorkflowTurn<ReservationState>.Request(
        state.RetryingRelease(),
        ReservationMessages.Release(context, state.First.ReservationId),
        continueAt: "first-released");
}
```

ReservationMessages returns an existing immutable OutgoingMessage. It does not enqueue.
Its payload aliases, routes, serializer options and W3C trace capture remain consumer-owned:

```csharp
internal static OutgoingMessage Release(WorkflowContext context, Guid reservationId) =>
    OutgoingMessage.FromPayload(
        context.CommandId,
        "inventory.release-reservation",
        "inventory.release-reservation",
        1,
        new ReleaseStockReservationV1(reservationId),
        Json,
        tenantKey: context.Key.TenantKey,
        correlationId: context.Key.Id.ToString("D"),
        causationId: (context.CauseMessageId ?? context.Key.Id).ToString("D"),
        traceParent: Activity.Current?.Id,
        traceState: Activity.Current?.TraceStateString);
```

The runner allocates CommandId before invoking a method and requires a returned request to
retain that ID, the supplied tenant and workflow correlation. A failed uncommitted attempt
may allocate another ID on retry; no committed outgoing operation is replaced. Stable business
reservation IDs are distinct and remain in the consumer state across operator retries.

Bindings are explicit and immutable after Build. They bind methods and wire reply contracts;
they do not build or interpret a graph, discover handlers, or persist delegates/CLR names.

```csharp
var definition = new WorkflowBuilder<StartStockReservationOrder, ReservationState>(
        "stock-reservation", definitionVersion: 1, Json)
    .StartWith(Start)
    .OnReply<StockReservationDecidedV1>(
        "first-reserved", "inventory.reservation-decided", 1, FirstReserved)
    .OnReply<StockReservationDecidedV1>(
        "second-reserved", "inventory.reservation-decided", 1, SecondReserved)
    .OnReply<StockReservationReleasedV1>(
        "first-released", "inventory.reservation-released", 1, FirstReleased)
    .OnSignal<RetryReservationRelease>("retry-release", RetryRelease)
    .Build();

services.AddWorkflow<SalesDbContext, StartStockReservationOrder, ReservationState>(
    provider => definition);

// Native DbContext model. JSONB is an explicit consumer provider choice.
model.ConfigureWorkflows("sales", "workflow_instances", jsonColumnType: "jsonb");

// Both native SaveChanges overrides, beside existing inbox/outbox validation.
this.ValidateWorkflowChanges();
```

These wire aliases are the existing StockReservationReplyAdmission registrations; business
Contracts and producer routes do not change. The definition factory supports constructing a
scoped consumer handler with normal injected
dependencies. No reflection-based method discovery or runtime code generation is introduced.

Direct application usage is small and keeps native transaction ownership:

```csharp
// Consumer has already admitted the tenant and actor and validated the business request.
var key = new WorkflowKey(request.OrderId, database.RequiredOrganizationKey);
await using var transaction = await database.Database.BeginTransactionAsync(token);

var snapshot = await workflow.StartAsync(key, request, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);

return ReservationViews.Read(snapshot);
```

The receiving handler keeps producer/tenant/actor admission, then delegates continuation:

```csharp
public async Task HandleAsync(IncomingMessage message, CancellationToken token)
{
    ReservationReplyAdmission.AdmitAndEstablishContext(message);

    var key = new WorkflowKey(Guid.Parse(message.CorrelationId!), message.TenantKey);
    await workflow.HandleReplyAsync(key, message, token);

    // Existing inbox processor saves state, next outgoing request and completion together.
}
```

The authorized operator path calls SignalAsync(key, new RetryReservationRelease(), token)
inside an explicit native transaction, then saves and commits. The stored signal binding is
chosen by the last WaitForInput result; the operator cannot select arbitrary methods or
resume a process still awaiting a reply.

## Proposed public surface

Core namespace: Gloaming.Messaging.Workflows. All public members receive XML documentation.
Names below form the proposed interface; implementation helpers remain internal.

```csharp
public sealed record WorkflowKey(Guid Id, string? TenantKey = null);

public sealed record WorkflowContext(
    WorkflowKey Key, Guid CommandId, Guid? CauseMessageId);

public enum WorkflowStatus
{
    Waiting = 1,
    Completed = 2,
    AwaitingInput = 3,
}

public sealed class WorkflowTurn<TState>
    where TState : class
{
    // Private construction; holds typed candidate state and at most one request.
    public static WorkflowTurn<TState> Request(
        TState state, OutgoingMessage command, string continueAt);
    public static WorkflowTurn<TState> Complete(TState state);
    public static WorkflowTurn<TState> WaitForInput(TState state, string continueAt);
}

public sealed class WorkflowBuilder<TStart, TState>
    where TStart : class
    where TState : class
{
    public WorkflowBuilder(string workflowType, int definitionVersion,
        JsonSerializerOptions serializerOptions);
    public WorkflowBuilder<TStart, TState> StartWith(
        Func<WorkflowContext, TStart, WorkflowTurn<TState>> start);
    public WorkflowBuilder<TStart, TState> OnReply<TReply>(
        string continuationName, string messageName, int schemaVersion,
        Func<WorkflowContext, TState, TReply, WorkflowTurn<TState>> handler)
        where TReply : class;
    public WorkflowBuilder<TStart, TState> OnSignal<TSignal>(
        string continuationName,
        Func<WorkflowContext, TState, TSignal, WorkflowTurn<TState>> handler)
        where TSignal : class;
    public WorkflowDefinition<TStart, TState> Build();
}

public sealed class WorkflowDefinition<TStart, TState>
    where TStart : class
    where TState : class
{
    // Immutable; created by Build. Binding internals are not public.
    public string WorkflowType { get; }
    public int DefinitionVersion { get; }
}

public sealed record WorkflowSnapshot<TState>(
    WorkflowKey Key, int DefinitionVersion, long Version,
    WorkflowStatus Status, string? ContinuationName,
    Guid? PendingCommandId, TState State)
    where TState : class;

public interface IWorkflow<TStart, TState>
    where TStart : class
    where TState : class
{
    Task<WorkflowSnapshot<TState>> StartAsync(
        WorkflowKey key, TStart input, CancellationToken cancellationToken = default);
    Task<WorkflowSnapshot<TState>> HandleReplyAsync(
        WorkflowKey key, IncomingMessage reply, CancellationToken cancellationToken = default);
    Task<WorkflowSnapshot<TState>> SignalAsync<TSignal>(
        WorkflowKey key, TSignal signal, CancellationToken cancellationToken = default)
        where TSignal : class;
    Task<WorkflowSnapshot<TState>?> ReadAsync(
        WorkflowKey key, CancellationToken cancellationToken = default);
}
```

The class constraints reflect JSON object contracts/state in both adopters; the first scope
does not design scalar/string workflow inputs, polymorphic arbitrary-object transitions or
transport messages from unregistered schemas. Serializer options are defensively copied and
frozen when building the definition. Serialization/required-field validation remains the
consumer's selected System.Text.Json policy. Incoming reply envelopes are owned existing
Messaging values; adapters establish producer trust before HandleReplyAsync.

EF namespace: Gloaming.Messaging.Workflows.EntityFrameworkCore.

```csharp
public sealed class EfWorkflow<TDbContext, TStart, TState> : IWorkflow<TStart, TState>
    where TDbContext : DbContext
    where TStart : class
    where TState : class
{
    public EfWorkflow(TDbContext database, IOutbox<TDbContext> outbox,
        WorkflowDefinition<TStart, TState> definition);
    // Interface implementation; no constructor I/O.
}

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflow<TDbContext, TStart, TState>(
        this IServiceCollection services,
        Func<IServiceProvider, WorkflowDefinition<TStart, TState>> definitionFactory)
        where TDbContext : DbContext
        where TStart : class
        where TState : class;
}

public static class WorkflowModelBuilderExtensions
{
    public static ModelBuilder ConfigureWorkflows(this ModelBuilder model,
        string schema, string tableName, string jsonColumnType);
}

public static class WorkflowSaveChangesExtensions
{
    public static void ValidateWorkflowChanges(this DbContext database);
}
```

The service helper aliases one scoped runner to IWorkflow<TStart,TState>. Its TDbContext
selects the exact owning context/outbox; no bare DbContext is resolved. Different input/state
pairs can bind different modules in one host. Reusing the same pair for another owning context
or definition is rejected rather than silently overriding DI; keyed variants are deferred.
Contexts, provider and existing outbox/processor registration remain consumer-owned.

One provided public WorkflowInstanceRecord has read-only properties/private setters and
library-controlled construction. Its native EF shape is:

| Property | Purpose |
| --- | --- |
| WorkflowType, Id | Composite primary key; ID is globally unique within that workflow type. |
| TenantKey | Nullable admitted scope; included in lookup and original-value concurrency checks. |
| DefinitionVersion | Required exact registered definition version. |
| Version | Scalar optimistic concurrency token; advances once for each accepted turn. |
| Status | Native-queryable technical waiting/completed/awaiting-input status. |
| ContinuationName, PendingCommandId | Exactly one pending reply, or the saved external-input continuation name. |
| InitialInput | Owned JSON for stable repeated-start comparison. |
| State | Owned JSON for restored typed business state. |
| Operations | Owned JSON ledger of requested commands and recorded reply contracts/results. |

```csharp
public sealed class WorkflowInstanceRecord
{
    // Private constructor; the runner supplies new instances. Native queries remain available.
    public string WorkflowType { get; private set; }
    public Guid Id { get; private set; }
    public string? TenantKey { get; private set; }
    public int DefinitionVersion { get; private set; }
    public long Version { get; private set; }
    public WorkflowStatus Status { get; private set; }
    public string? ContinuationName { get; private set; }
    public Guid? PendingCommandId { get; private set; }
    public JsonElement InitialInput { get; private set; }
    public JsonElement State { get; private set; }
    public JsonElement Operations { get; private set; }
}
```

Native scalar queries over the provided row remain available. Generic CLR queries over typed
JSON business state are not promised. Index/table customization remains native EF. The first
scope retains history in the row; pruning, high-volume history storage and throughput claims
are deferred. A different tenant reusing the same ID cannot load or continue the instance;
duplicate-key start faults require rollback and do not reveal its state. Equal GUIDs in two
tenants of the same workflow type are not supported by the selected global instance identity.

## Execution and transaction contract

1. Writes require the owning explicit native transaction, reject ambient/enlisted substitutes,
   and reject changed/reused tracked process proposals. The runner never starts/saves/commits
   a transaction or invokes a publisher. ReadAsync is a detached single-instance lookup.
2. Start checks the type/key/tenant, serializes input and compares with retained initial input
   before calling Start. Equal duplicate returns current state without another command;
   changed input for the same key is rejected. Competing first creates use database uniqueness;
   recovery loads the committed instance in a fresh scope.
3. Continue checks correlation/tenant and causation against a requested command. The selected
   continuation's alias/schema must match. Consumer admission handles producer trust; the
   retained result includes producer, alias, schema and semantic JSON payload. Delivery ID,
   trace metadata and object-property ordering do not distinguish equal operation outcomes.
4. Equal old results return current state without invoking business code or changing Version.
   Conflicting old results and unknown operations fail. Previously refused results stay
   recorded when an operator requests a new compensation command; an old result cannot
   overwrite the newer operation. Historical continuation methods are never replayed.
5. A new continuation receives deserialized candidate state, not the tracked row's mutable
   state. Its request/next binding/state serialization are validated before tracking changes.
   A returned request must use context.CommandId, the same tenant and workflow correlation.
   Request outcomes do not imply success: consumer code validates business fields/decisions.
6. The runner advances scalar Version, updates the process row and enqueues its one returned
   command through the existing IOutbox<TDbContext>. Native save validation binds that proposal
   and its expected tracked outgoing row to the current context/transaction. Removing or
   changing the outgoing command, mutating process identity/version/state, switching transaction
   or direct unregistered process writes are rejected when the guard is installed.
7. SignalAsync is available only at AwaitingInput and invokes its configured signal binding
   with the matching signal type. It advances Version once; a returned request allocates a new
   command identity while consumer business identities stay unchanged. Operator authorization
   is consumer policy. Resume signals are not inbox deliveries or an automatic retry loop.
8. Existing InboxProcessor commits local state, next command and delivery completion together.
   Start/Resume callers explicitly save/commit. Native conflicts, SQL faults and cancellation
   require disposal/rollback and a fresh operation scope. The existing inbox retry policy stays
   in charge; the new runner supplies no second retry scheduler.

Only one pending request per instance and one accepted turn per instance/context operation
are supported. Bindings are synchronous ordinary C# decision methods; logging is allowed, but
external effects and saving/committing from bindings are outside the supported contract.
Uncommitted attempts can execute/log twice. Native rollback does not undo a caller's local
objects or logs. No exactly-once external effect, message ordering, global transaction,
parallel-context usage or universal provider promise is made.

No timeout, scheduling, graph interpretation, function replay, sub-workflow, automatic reverse
undo, arbitrary signals while waiting, cancellation of committed requests, leader election,
worker orchestration, inbox replacement or outbox replacement is introduced. A missing reply
remains waiting. This version supports one registered definition version per binding; incompatible
updates fail rather than execute new code against old instances. Concurrent multi-version
registration, running-instance migrations and pruning are deferred.

## Errors and early feedback

- ArgumentException/ArgumentOutOfRangeException: empty identity/name, invalid positive versions,
  missing handlers or invalid configuration; duplicate names/wrong registration fail early.
- InvalidOperationException: no owning transaction, incompatible model/DI, wrong lifecycle,
  changed duplicate start input, unsupported Resume state or changed tracked proposal.
- InvalidDataException: unknown operation, conflicting outcome, mismatched reply identity or
  alias/schema, missing continuation, incompatible stored definition/state/operation ledger,
  or request metadata that does not match the execution context.
- JsonException: consumer-required fields/converters reject start/state/reply JSON.
- Native DbUpdateConcurrencyException/DbUpdateException and OperationCanceledException:
  propagated with native rollback/disposal and fresh-scope recovery responsibilities.

These checks enforce the supported protocol, not every possible direct SQL mutation. There is
no middleware that can infer producer trust, missing operator permissions or business validity.

## Materially different adopter

Replace the standalone workspace saga proof with a tenantless ordinary EF consumer. Its first
request chooses either an existing-resource validation path or a new-resource creation path,
depending on input/reply state. An activation refusal compensates only resources the workflow
actually created. This is a real branch that cannot be expressed by renaming the existing
fixed sequential plan. A refused release retains an explicit operator retry point.

The standalone application uses its own context/schema, JSON options, aliases and fake resource
reply contracts, real PostgreSQL and existing inbox/outbox. It does not adopt sample Contracts,
Organization, event sourcing or RabbitMQ. It proves continuation/storage protocol, not real
external resource provisioning. Sales retains the real PostgreSQL/RabbitMQ process journey.

## Exact replacement/file scope for review

| Files | Change |
| --- | --- |
| Every tracked/untracked file in `src/Gloaming.Messaging/Gloaming.Messaging.Sagas/` | Replace the uncommitted project/README and seven sequential types with `Gloaming.Messaging.Workflows/`: key, context, status, turn, builder, immutable definition, snapshot and workflow interface; internal bindings only. No old API compatibility layer. |
| Every file in `src/Gloaming.Messaging/Gloaming.Messaging.Sagas.EntityFrameworkCore/` | Replace the uncommitted mapping project/README with `Gloaming.Messaging.Workflows.EntityFrameworkCore/`: provided row, model validation, native runner, scoped registration and save guard/registry. |
| `src/Gloaming.Messaging/tests/SagaTests/{SagaTests.csproj,SequentialSagaTests.cs}` | Replace with WorkflowTests project and pure binding/turn/serialization/definition cases. |
| `src/Gloaming.Messaging/tests/SagaPostgresTests/{SagaPostgresTests.csproj,SagaConsumer.cs,ProgressTests.cs}` | Replace with WorkflowPostgresTests project, tenantless branching consumer and real PG protocol/recovery proofs. Preserve transferred failure assertions; report which old sequential-policy tests no longer apply. |
| `samples/Wholesale/modules/Sales/Sales/ReservationOrders/{StockReservationOrder.cs,StockReservationOrderMapping.cs}` | Replace the uncommitted EF process entity/mapping with typed ReservationState and ReservationWorkflow continuation/binding code. No business aggregate is required by the runtime. |
| `.../ReservationOrders/{StockReservationOrders.cs,StockReservationReplyHandler.cs,StockReservationReplyAdmission.cs}` | Start/read/authorized-retry call typed runner; message helper returns envelopes; ingress retains admission, library handles continuation lookup/decode/bookkeeping/enqueue. |
| `samples/Wholesale/modules/Sales/Sales/{Sales.csproj,SalesDbContext.cs,SalesRegistration.cs}` | Select new optional workflow adapter; configure provided process table/JSONB/save guard and typed definition. Other Sales behaviors unchanged. |
| `samples/Wholesale/modules/Sales/Sales/Migrations/{20261010164619_AddCompensatingReservationOrders.cs,20261010164619_AddCompensatingReservationOrders.Designer.cs,SalesDbContextModelSnapshot.cs}` | Replace only the still-uncommitted WF2 migration draft with the provided process-table shape. Preserve all earlier checkpointed migrations. No installed database is reset; an installed old draft would need a separately reviewed forward migration. |
| `samples/Wholesale/IntegrationTests/Reservations/Broker/{ReservationTests.cs,ReservationFailureTests.cs,ReservationRecoveryTests.cs}` | Adapt native table/ledger probes and barriers, retain public behavior/failure/kill/restart tests; add no-replay and binding mismatch proofs. |
| `tests/ArchitectureTests/{ArchitectureTests.csproj,AdoptionDependencyTests.cs,AssemblyDependencyTests.cs,SampleModuleBoundaryTests.cs}` | Replace optional saga identities with workflow dependency/independent-adoption checks. |
| `ModulithFoundry.slnx`, `.github/workflows/ci.yml` | Replace the two optional projects and their two test lanes/paths; existing family path filtering remains. |
| `src/Gloaming.Messaging/{README.md,docs/capabilities.md,docs/sagas.md}` | Update optional package list/current contract; replace the uncommitted saga guide with `docs/workflows.md`. |
| `samples/Wholesale/Worker/Broker/{README.md,compensating-reservations.md}` | Explain new execution usage and existing transport/local-transaction ownership. |
| `samples/Wholesale/Worker/Broker/Program.cs` | Implementation follow-through: update the existing startup SQL probe to the replacement Sales table. No hosting/worker protocol changes. |
| `docs/{design.md,plans/library-extraction.md,plans/remaining-capability-roadmap.md,adr/0013-fixed-sequential-saga-progress.md}` | After interface approval, record replacement/supersession, current capability and deferred features; retain earlier proposal/report evidence as historical. |
| `docs/plans/wf2-durable-continuations.md`, `docs/reports/wf2-durable-continuations.md` | This concrete proposal and subsequent fresh implementation/adoption report. |

No Inventory behavior, public business Contracts, existing envelope/outbox/inbox interfaces,
worker runtime, frozen archive, fixtures, prior migrations, template or unrelated staged files
are removed or changed by this replacement. The whole staged snapshot stays byte-for-byte
unchanged; approved removals/renames become working-tree changes until the owner reviews them.

## Required fresh verification

Pure checks: registration/name/type errors, immutable definitions/options, expected-reply
bindings, required JSON fields and invalid returned requests, plus ordinary continuation unit
tests that assert requests/state without mocking DbContext or publisher.

Real PostgreSQL consumer checks: exact state/version/command consistency, one new turn per
reply, old duplicate/conflicting results, repeated and competing starts, competing replies
and operator resumes, state/outbox/inbox completion rollback, explicitly removed outgoing
rows and transaction replacement, rejected business replies, tenant scope, definition mismatch,
outbox failure, cancellation and fresh-context recovery. Prove the independent branch and
only invoke the saved continuation after reload. Do not claim provider/transaction correctness
from the throwaway browser simulation.

Sales real PG/RabbitMQ checks: retain both reservation success, second refusal/release,
intervening Inventory changes, refused release/operator retry, old results, native competing
writers, rollback and killed-host recovery. Existing module persistence/event consumers,
Messaging unit/outbox/inbox PostgreSQL and architecture suites remain relevant.

Run solution build, CSharpier, native style/analyzers, whitespace, documentation links, archive
integrity and the existing family CI checks affected by the replacement. Report fresh runs
separately from the earlier 584 sequential-experiment cases. No commit or staging is authorized
by interface approval.
