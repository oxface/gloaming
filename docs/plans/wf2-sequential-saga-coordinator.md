# WF2 follow-up: bounded sequential saga coordinator

Historical experiment, superseded before checkpoint by the owner-approved
[continuation replacement](../plans/wf2-durable-continuations.md). Old test counts are dated
evidence, not results for the replacement. The staged review snapshot is retained.


Status: interface and replacement scope owner-approved on 2026-10-10 by the instruction to
proceed after proposal review. Implemented on `feat/compensating-workflow` for owner code review;
[the report](../reports/wf2-sequential-saga-coordinator.md) records fresh library/adoption proofs.
The existing staged review snapshot is preserved; new edits remain in the working tree.

## What earns extraction

The current Sales process (historical source, replaced by WF2 continuations)
mixes business input matching with reusable progress bookkeeping: expected command IDs,
recorded outcomes, duplicate/conflict handling, ordered advancement, release attempts and
protection against an older attempt overwriting a newer result. Those obligations can move
together without moving Inventory's reserve/release rules or Sales' wire Contracts.

The proposed coordinator supports a fixed ordered sequence of named steps. A committed
business refusal starts compensation of previously successful steps in reverse order.
Each compensating command must be acknowledged before requesting the next. Technical
exceptions and missing replies leave the current action pending; neither means refusal.

This is narrower than a general saga state machine. The existing WF1 deadline workflow
stays consumer-owned: its overdue status and detailed outcome comparison do not become a
generic timeout policy merely to demonstrate another adoption.

The [archived fulfilment process](../../archive/proof-sample/modules/Sales/Sales/Fulfilment/OrderFulfilmentProcess.cs)
also tracks versions, line outcomes and release progress, but has cancellation, replenishment
and overlapping pending lines. It supplies historical obligation evidence, not a consumer
that this sequential interface claims to replace. No archived code changes or test runs.

Wolverine's [saga documentation](https://wolverinefx.net/guide/durability/sagas) separates
persisted state, messages, identity and persistence, and demonstrates generated loading and
saving around handlers. Our inference is that durable progress is independently useful;
we can reuse existing explicit EF/inbox/outbox composition instead of adopting its discovery,
generated handlers or bus runtime. Wolverine remains a reference, not a dependency.

## Proposed packages and public types

`Gloaming.Messaging.Sagas` is optional and package-free. It uses platform
System.Text.Json only for its own durable progress document. It does not depend on the
Messaging envelope, EF, PostgreSQL, Events, EventSourcing, tenancy, DI or a transport.

`Gloaming.Messaging.Sagas.EntityFrameworkCore` is an optional mapping adapter, depending on
the saga core and Microsoft.EntityFrameworkCore. It converts and snapshots that document
and installs native optimistic concurrency on the mapped property. PostgreSQL JSONB remains
explicit consumer configuration. No provider package, store registry or new worker is needed.

Reviewed public surface, with member summaries retained from interface review:

```csharp
namespace Gloaming.Messaging.Sagas;

public enum SagaCompensation
{
    NotRequired = 1, // The consumer explicitly declares no undo is necessary.
    Command = 2,     // Acknowledged compensation is required after a later refusal.
    Unsupported = 3, // An accepted effect cannot be automatically compensated.
}

public enum SagaActionKind
{
    Execute = 1,
    Compensate = 2,
}

public enum SagaStatus
{
    Running = 1,
    Completed = 2,
    Compensating = 3,
    Compensated = 4,
    Failed = 5,
    NeedsAttention = 6,
}

public sealed record SagaStep(string Name, SagaCompensation Compensation);

// CommandId identifies this requested attempt, not the business resource being reserved.
public sealed record SagaAction(Guid CommandId, string StepName, SagaActionKind Kind);

// Code distinguishes different business refusals as well as accepted results.
// The consumer still validates the entire reply against its requested business inputs.
public sealed record SagaOutcome(bool Succeeded, string Code);

public sealed class SequentialSaga
{
    // Copies the plan and requests the first action. Initial Version is 1.
    public static SequentialSaga Create(params SagaStep[] steps);

    public SagaStatus Status { get; }
    public long Version { get; }
    public SagaAction? PendingAction { get; }
    public SagaOutcome? Failure { get; }
    public SagaOutcome? CompensationFailure { get; }
    public string? AttentionStep { get; }

    // Finds a requested action, including an older completed attempt.
    public SagaAction GetAction(Guid commandId);

    // Records a validated business outcome and returns only a newly requested action.
    // Equal duplicate: no change, no version increase and null return.
    public SagaAction? RecordOutcome(Guid commandId, SagaOutcome outcome);

    // After a refused compensating command, requests a new attempt of the same step.
    public SagaAction RetryCompensation();

    // Versioned library state format; no message payloads or application entity identity.
    public string Serialize();
    public static SequentialSaga Deserialize(string state);
}
```

`SequentialSaga` has a private constructor and no public property setters. Its internal
step/attempt ledger is copied and validated before accepting a transition. Invalid input
must leave the previous progress untouched. Every changed transition increments Version
once; requesting a new compensation attempt also increments it once. Creating command IDs
is library bookkeeping; stable reservation/resource IDs remain consumer-owned.

The ordered plan is persisted with progress, so changing the code for new workflows does
not silently change the order of existing ones. Unknown step names must fail the consumer's
message mapping. Migrating a running plan is deferred. Stored progress uses an explicit
format version and validates identities, enum values and ledger consistency when restored.

```csharp
namespace Gloaming.Messaging.Sagas.EntityFrameworkCore;

public static class SequentialSagaPropertyBuilderExtensions
{
    // Installs conversion, a deep snapshot comparer, and a native concurrency token.
    // Does not choose the table, column type, tenant filter or transaction policy.
    public static PropertyBuilder<SequentialSaga> HasSequentialSaga(
        this PropertyBuilder<SequentialSaga> property);
}
```

The comparer is necessary because progress changes inside the same object. EF must compare
with an independent original snapshot, rather than the mutated object itself. Updates compare
the original serialized document in the database predicate. This trades a larger concurrency
predicate for avoiding another required entity interface or version property adapter.
Version remains available for the business Contract. PostgreSQL is the first proved provider;
native equality/column mapping on any other provider needs its own proof.

This conversion does not make `Progress.Status` or ledger members translatable in native
LINQ queries. The current consumer loads by its ordinary tenant/order key. A later monitor
needing indexed status selection should review a relational status projection rather than
pretend this serialized property supplies one. No saga-scanning worker is introduced here.

## Concrete consumer after extraction

The Sales entity keeps Organization/order/reservation identities and both requested lines.
It has one mapped `SequentialSaga Progress` property. Status and refusal fields in its public
read Contract are derived from progress; business request matching remains explicit.

```csharp
// Creation: Sales chooses the sequence and says which effects require release.
Progress = SequentialSaga.Create(
    new SagaStep("first-reservation", SagaCompensation.Command),
    new SagaStep("second-reservation", SagaCompensation.Command));

// Native mapping in Sales, with the existing tenant-owned root key/filter.
order.Property(row => row.Progress)
    .HasSequentialSaga()
    .HasColumnType("jsonb");

// A validated outcome in the existing Sales inbox handler.
var requested = process.Progress.GetAction(cause);
process.ValidateReservationReply(requested, decided);

var next = process.Progress.RecordOutcome(
    cause,
    new SagaOutcome(
        decided.Decision == StockReservationDecision.Reserved,
        decided.Decision.ToString()));

if (next is not null)
    messages.Enqueue(process, next, message.MessageId);

// Existing inbox processor performs SaveChanges, completion and commit.
```

`messages.Enqueue` is a consumer switch on StepName/Kind, producing existing typed
ReserveStockV1/ReleaseStockReservationV1 payloads. CommandId becomes MessageId, the owning
order ID becomes CorrelationId, and the incoming reply ID becomes CausationId. Stable
reservation IDs come from the Sales entity. The library does not guess routes or serialize
business payloads. A released outcome follows the same GetAction/validate/RecordOutcome flow.

Finite start and explicit operator retry still open a native Sales transaction, enqueue the
returned action with `IOutbox<SalesDbContext>`, save and commit. RetryCompensation returns a
new command identity while the release payload targets the original reservation identity.
No DI registration is required for the saga value or mapping helper. Existing typed contexts,
outbox/inbox registration and hosted workers remain in use.

## Guarantees, errors and ownership

- Exactly one requested action is pending. A forward refusal compensates only successful
  earlier steps. A success of the last forward step completes the sequence.
- Compensation runs backward and waits for each acknowledged result. NotRequired steps are
  skipped by explicit consumer declaration. Encountering Unsupported stops with NeedsAttention
  and AttentionStep; it cannot claim full compensation. A refused undo also stops there.
- RetryCompensation is allowed only after a refused command compensation. It cannot make an
  unsupported undo possible. No timer, automatic business retry or attempt cap is introduced.
- Matching old outcomes are no-ops even after later progress. Different Succeeded/Code for
  one command is InvalidDataException. Unknown/unrequested causes are InvalidDataException.
  The consumer must separately reject replies whose business IDs/inputs do not match.
- Invalid plans/arguments use native ArgumentException variants; invalid state operations use
  InvalidOperationException. Bad or unsupported stored state uses InvalidDataException.
  Native EF concurrency/database exceptions propagate; a failed processing scope is discarded.
- Core operations perform no I/O and provide no thread-safety or durability on their own.
  The mapped property supplies optimistic arbitration for writers using that mapping. Existing
  inbox/outbox and the consumer transaction supply atomic progress/next-command/completion.
  The utility cannot guarantee that custom code remembers to enqueue its returned action.
- Context creation, tenant/actor admission, business result interpretation, message creation,
  routing, semantic resource receipts, external effects, save/commit, retention and operator
  authorization remain with the consumer or the already adopted messaging mechanism.
- There is no distributed transaction or rollback. Missing replies and exceptions are unknown
  outcomes and leave progress pending. They never automatically trigger compensation.

## Exact proposed file and behavior map

| Files | Change |
| --- | --- |
| New `src/Gloaming.Messaging/Gloaming.Messaging.Sagas/{Gloaming.Messaging.Sagas.csproj,SequentialSaga.cs,SagaStep.cs,SagaAction.cs,SagaOutcome.cs,SagaCompensation.cs,SagaActionKind.cs,SagaStatus.cs,README.md}` | Package-free bounded engine and versioned state serialization. Any serializer/ledger helpers stay internal. XML documentation on every public member. |
| New `src/Gloaming.Messaging/Gloaming.Messaging.Sagas.EntityFrameworkCore/{Gloaming.Messaging.Sagas.EntityFrameworkCore.csproj,SequentialSagaPropertyBuilderExtensions.cs,README.md}` | Used native property conversion, independent change-tracker snapshot and optimistic concurrency; no store or transaction facade. |
| `samples/Wholesale/modules/Sales/Sales/ReservationOrders/{StockReservationOrder.cs,StockReservationOrders.cs,StockReservationReplyHandler.cs,StockReservationOrderMapping.cs}` | Replace technical status/command/attempt bookkeeping with Progress; retain request validation, business IDs and explicit typed message mapping. Remove StockReleaseAttempt and ReservationOrderAction when their roles are replaced. |
| `samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs`; same project's `Sales.csproj` | Update mapping and opt into the two optional packages. Inventory and Sales Contracts remain unchanged. |
| New, uncommitted WF2 Sales migration `20261010164619_AddCompensatingReservationOrders.cs`, its designer, and current `SalesDbContextModelSnapshot.cs` | Owner-approved replacement of this slice's draft schema: one Progress JSONB column replaces technical root fields and the draft release-attempt table. Keep business fields. No earlier/checkpointed migration or Inventory migration is rewritten. |
| `samples/Wholesale/WorkflowDemo.Tests/{ReservationTests.cs,ReservationFailureTests.cs,ReservationRecoveryTests.cs,ReservationProof.cs}` | Preserve external behavior/assertions; update implementation-specific probes/barriers for the new Sales progress column. All existing WF1 cases remain. |
| New `src/Gloaming.Messaging/tests/SagaTests/{SagaTests.csproj,SequentialSagaTests.cs}` | Core contract: rejection leaves state unchanged, duplicate/conflict behavior, reverse compensation, unsupported undo, explicit retry, state round-trip and plan retention. |
| New `src/Gloaming.Messaging/tests/SagaPostgresTests/{SagaPostgresTests.csproj,SagaConsumer.cs,ProgressTests.cs}` | Standalone typed-context adopter without Inventory, EventSourcing, tenancy or RabbitMQ: refusal of a third resource operation requires two acknowledged reverse undos; a separate variant skips an explicitly NotRequired step. Real PostgreSQL proves converter tracking, competing replies, rollback, fresh-context resume and atomic next outbox work. It is a contract adopter, not another full product workflow. |
| `ModulithFoundry.slnx`; `tests/ArchitectureTests/{ArchitectureTests.csproj,AssemblyDependencyTests.cs,AdoptionDependencyTests.cs,SampleModuleBoundaryTests.cs}` | Include used projects; prove package dependencies, module isolation and independent adoption. |
| `Directory.Packages.props` | Pin the adapter's actually used Microsoft.EntityFrameworkCore dependency to the existing EF version 10.0.12. |
| `.github/workflows/ci.yml` | Add both saga suites to the existing Messaging lane. Existing `.github/ci-paths.yml` family glob already covers these paths; no new script or runtime lane. |
| `src/Gloaming.Messaging/{README.md,docs/capabilities.md,docs/sagas.md}`; WorkflowDemo usage; WF2 plan/report; extraction plan/roadmap | Self-contained setup/guarantees/deferred context and new evidence, clearly separate from the earlier 474-case consumer-only run. |

The draft Sales migration replacement is confined to the uncommitted WF2 increment. It does
not authorize resetting an installed demo database. An existing database that has applied
that draft migration would need a separately reviewed forward data migration before reuse.

## Verification and limits

Rerun the complete WorkflowDemo suite after adoption, including real broker/process recovery,
competing writers and rollback. Prove the new mapping and three-step/partial-compensation
behavior independently through real PostgreSQL. Run existing Messaging suites, architecture,
event consumers affected by the overall WF2 branch, native formatting/analyzers/build and
archive verification. Run relevant generated-template checks if architecture/project changes
affect their dependency assertions; do not introduce a saga template preset.

Keep the core's state-machine tests focused on externally observable transitions and retained
outcomes, not private ledger layout. No new concurrency/transaction guarantee is supported
until the PostgreSQL tests pass. The earlier WF2 474 passing cases prove the existing consumer
implementation only, not this proposal.

Parallel steps, branching, cancellation of in-flight work, timeout-driven decisions,
compensation after successful completion, scheduling, workflow-plan migrations, retention,
operator tooling and universal saga storage stay deferred. Fixed sequential progress is an
optional utility; other workflows retain ordinary consumer state machines. If the adoption
still needs comparable bookkeeping or makes business validation harder, report that finding
and stop rather than layering another abstraction on top.
