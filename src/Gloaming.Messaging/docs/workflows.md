# Durable continuations

Optional `Gloaming.Messaging.Workflows` and `Gloaming.Messaging.Workflows.EntityFrameworkCore`
execute named consumer methods from a stored process. A new reply invokes only its saved
continuation. Start and earlier methods are not replayed. Business branching and compensation
are ordinary C# decisions; there is no graph interpreter, scheduler or workflow worker.

## Setup and execution

A module registers its own typed context, existing typed outbox and definition:

```csharp
services.AddWorkflow<OrdersDbContext, StartOrder, OrderState>(_ =>
    new WorkflowBuilder<StartOrder, OrderState>("order", 1, Json)
        .StartWith(Start)
        .OnReply<ReservationReply>("reserved", "stock.reserved", 1, Reserved)
        .OnSignal<RetryReservation>("retry-reservation", Retry)
        .Build());

// OnModelCreating: schema/table belong to this module. JSONB is an explicit provider choice.
modelBuilder.ConfigureWorkflows("orders", "workflow_instances", "jsonb");

// Both SaveChanges overrides, alongside existing outbox/inbox guards:
this.ValidateWorkflowChanges();
```

`Start` or a continuation returns one of three typed instructions:

```csharp
static WorkflowTurn<OrderState> Reserved(
    WorkflowContext context, OrderState state, ReservationReply reply)
{
    state.ValidateBusinessReply(reply); // consumer policy

    if (reply.Succeeded)
        return WorkflowTurn<OrderState>.Complete(state with { Status = "reserved" });

    return WorkflowTurn<OrderState>.WaitForInput(
        state with { Status = "refused" }, "retry-reservation");
}

static WorkflowTurn<OrderState> Retry(
    WorkflowContext context, OrderState state, RetryReservation signal) =>
    WorkflowTurn<OrderState>.Request(
        state with { Status = "requested" },
        OutgoingMessage.FromPayload(
            context.CommandId, "stock.reserve", "stock.reserve", 1,
            state.Request, Json,
            tenantKey: context.Key.TenantKey,
            correlationId: context.Key.Id.ToString("D"),
            causationId: context.CauseMessageId?.ToString("D")),
        continueAt: "reserved");
```

Direct starts and operator resumption retain native transaction ownership:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var snapshot = await workflow.StartAsync(new(orderId, admittedTenant), input, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

The admitted inbox handler calls `HandleReplyAsync(key, message, token)`. The existing inbox
processor saves process changes, next outgoing request and inbox completion, then commits.
No new workflow worker is registered. Existing outbox workers publish requests, while the
consumer receiver durably admits replies and acknowledges transport only after intake commit.

`WorkflowTurn.WaitForInput(state, continueAt)` persists a pause with status `AwaitingInput`.
`OnSignal<TSignal>` binds typed external input to that named continuation. `SignalAsync(key,
signal, token)` loads saved state and invokes the **stored** binding with the supplied input.
The mechanism can represent operator recovery or a deliberate pause for human approval; Sales
currently demonstrates compensation retry. The caller owns input admission, authorization and
save/commit. Retain any input needed later in the consumer state. A returned request receives a
new technical command ID; stable business resource IDs remain in state.

Signals are explicit calls without a durable signal-deduplication receipt: after an ambiguous
commit, read current progress before deciding to retry. `HandleReplyAsync` instead accepts an
admitted incoming envelope and matches its causation to the requested outgoing operation.
Signals are accepted only while `AwaitingInput`, replies only for their pending/retained
operations. No approval UI, assignment system or scheduled input worker is supplied.

`ReadAsync` is detached and performs no execution or writes. A returned write snapshot is a
proposal, not evidence of commit. Scalar provided-row fields are available to native EF queries;
portable CLR LINQ queries over the typed JSON state are not supplied.

## Guaranteed protocol and integration obligations

- One instance has at most one pending request. Its saved continuation binds a stable wire alias,
  schema and exact reply type. No persisted CLR names, reflection discovery or runtime codegen.
- Instance PK is `(WorkflowType, Id)`. IDs are unique within a type **across tenants**; TenantKey
  is optional opaque metadata, part of every runner lookup and an original-value concurrency
  token. It does not authenticate callers. Configure consumer admission/RLS separately if needed.
- Equal retained initial input returns current progress without invoking Start again, generating
  business IDs or enqueueing. Different input under the same identity is rejected.
- Requests retain `WorkflowContext.CommandId`, admitted TenantKey and instance correlation.
  Replies match tenant/correlation and requested command causation. Payload routes/serialization,
  producer trust and full business outcome validation belong to the consumer.
- Retained outcomes compare producer, alias, schema and semantic JSON. Object property order,
  delivery ID and trace metadata do not alter a business outcome. Equal older outcomes return
  current progress; conflicting or unrequested outcomes fail. Older refusals cannot replace a
  newer operator retry. Arrays and actual values remain significant.
- Candidate state is deserialized separately from tracked JSON. The runner validates returned
  instructions and serializes state/ledger before proposing tracked changes or enqueueing.
  A consumer method failure does not mutate the loaded process row. Methods must not save,
  commit, publish or perform external effects; logging observes attempts and may repeat after rollback.
- Writes require an explicit native EF transaction. Ambient/enlisted transactions are rejected.
  Each accepted turn increments scalar Version; native optimistic concurrency resolves competing
  writers. The loser discards its context/transaction and retries from fresh committed state.
- **Install ValidateWorkflowChanges in both native SaveChanges overrides**, plus existing
  outbox/inbox guards. It rejects direct tracked row edits, altered/detached process/request
  proposals and changed transaction ownership. It cannot constrain raw SQL, deliberately bypassed
  guards or arbitrary external side effects. One accepted turn per instance/scope; discard the
  scope after a proposal or database failure. Pooled leases do not inherit prior proposals.
- Guard metadata is attached to the owning DbContext through ConditionalWeakTable. A live key
  keeps its metadata alive; the table itself does not keep an otherwise unreachable context
  alive, even through a metadata reference back to the context. Pooled contexts stay alive
  through the pool, so the registry also clears proposals when ContextId changes to a new lease.
- EF only tracks changes. The caller's save/commit or existing inbox processor provides atomic
  persistence. There is no distributed transaction or exactly-once external effect claim.

Consumer JSON options are copied/frozen and bindings are copied at construction/build. Use
explicit `[JsonRequired]` fields and consumer validation where missing fields would otherwise
become a valid-looking default. No payload/state upcasting or automatic definition migration is
supplied. Incompatible persisted DefinitionVersion fails; deploy an explicit migration or retain
compatible definitions before changing it.

## Errors and limits

Argument and definition errors use native argument exceptions. Missing configuration/transaction,
reused proposals, changed start input, invalid returned instructions or unavailable operator
resumption use InvalidOperationException. Unknown/conflicting replies, mismatched metadata,
business-validation failure and incompatible stored lifecycle/version use InvalidDataException.
Required fields and serializers may throw JsonException. Cancellation and native EF/provider faults
propagate; rollback is the owning transaction's responsibility.

Real PostgreSQL is the verified provider. The EF package has no Npgsql dependency and accepts an
explicit JSON column type; this does not establish SQL Server/SQLite support. Consumer migrations
own the provided table. Existing deployments of the discarded uncommitted experiment would need
an explicit forward migration; the replacement does not reset a database.

The JSON operation ledger grows with accepted requests and retains outcomes indefinitely. No
throughput, bounded-history or pruning claim is made. One registered definition version is used
per binding. Parallel requests/joins, deadlines, cancellation, scheduled signals, durable operator
commands, graph/replay engines, workflow state upgrades, retention, dashboards and generic
compensation selection are deferred. Missing replies do not imply business failure. Existing inbox
retry has no workflow-specific poison policy. These are separate proofs before runtime expansion.

## Executable references

- [Sales bindings](../../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/ReservationWorkflow.cs)
  perform two stock reservations and explicitly release the first after second-line refusal.
- [Sales application](../../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOrders.cs)
  shows explicit start/read/operator save/commit. Its [reply handler](../../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationReplyHandler.cs)
  retains producer/tenant/actor admission.
- [Standalone workspace consumer](../tests/WorkflowPostgresTests/WorkspaceConsumer.cs) owns a different
  schema, tenantless metadata, JSON and contracts. Existing-resource inspection branches differently
  and refuses activation without releasing a resource it did not create.
- [Protocol/transaction/inbox proofs](../tests/WorkflowPostgresTests) exercise real PostgreSQL.
  [The dated slice report](../../../docs/reports/wf2-durable-continuations.md) separates these results
  from historical sequential-policy evidence.
