# Finite durable due-time intents

`PostgresDueIntents<TContext>` retains a finite occurrence and its original outgoing envelope,
suppresses pending work, and stages bounded eligible handoff to the same context's outbox.
It belongs to `Gloaming.Messaging.EntityFrameworkCore.Postgres`; no additional dependency or
Hosting package is required. The caller owns admission, source/business concurrency, native
ReadCommitted transaction, save, commit and repetition. No clock or publisher runs implicitly.

## Native setup

Map one provided occurrence table and one provided PostgreSQL outbox in the owning context:

```csharp
model.ConfigurePostgresOutbox("personnel", "outgoing");
model.ConfigurePostgresDueIntents("personnel", "due_intents");
```

Consumer migrations create both tables. Schema/table identifiers must fit PostgreSQL's 63-byte
limit without truncation. Keep the provided complete key, columns, JSONB, ordinal `C` scope,
state concurrency token, unconditional unique MessageId and microsecond timestamp mapping.
Additional filters/RLS and native indexes are consumer-owned. Converted keys, extra mapped
properties, inheritance, owned/split/view mappings and reduced due-time precision are unsupported.
Constructing the operations validates metadata without opening a database connection.

Install the model-selected save integration in native scoped context options:

```csharp
services.AddPostgresDueIntents<PersonnelDatabase>();
services.AddDbContext<PersonnelDatabase>((provider, options) =>
    options.UseNpgsql(connection)
        .ConfigureGloamingCapabilities<PersonnelDatabase>(provider));
```

This registers due-intent and outbox validation, with native asynchronous reads in async saves.
Alternatively call `this.ValidateDueIntentChanges()` before synchronous base saves and
`await this.ValidateDueIntentChangesAsync(cancellationToken)` before asynchronous base saves.
The direct validator also validates the outbox. These two native overrides cover both ordinary
and false-acceptance save overloads. A sync validator used in an async override performs sync I/O.
No integration opens transactions or commits. Raw/bulk SQL and deliberately omitted save wiring
remain outside the tracked-write contract.

## Occurrence, delivery and scope

Producer payloads must have unique object property names at every nesting level. JSONB discards
earlier duplicate properties; Stage rejects that ambiguous shape before I/O so proposed/stored
envelopes and repeated native saves agree. This structural requirement does not interpret payload
names, schemas or business meaning. Other native JSONB input limits still apply at save time.

`DueIntentKey(scopeKey, scheduleId, occurrenceId)` identifies one retained occurrence. Scope is
an explicit nonblank ordinal key of at most 256 .NET characters; the two IDs are nonempty GUIDs.
The schedule identifies the consumer's source; a replacement uses a new occurrence. The outgoing
MessageId independently identifies delivery. Stage inserts a new occurrence; it does not infer
duplicate input equality or recover an earlier business result. Native uniqueness rejects reused
retained keys/delivery IDs during ordinary saving. A separately selected logical command receipt
can preserve the outcome of an uncertain reschedule; this module takes no receipt dependency.

Scope selects technical work. Optional envelope TenantKey is retained independently and can be
null for tenantless work. Neither grants authority, selects an Organization or establishes an
operation context. Consumers admit the requested scope and configure any database policy.

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
// Lock/check the source and decide the new generation using consumer-owned business rules.
var intents = new PostgresDueIntents<PersonnelDatabase>(database);
await intents.SuppressAsync(previousKey, token);
intents.Stage(newKey, explicitlyCalculatedDueAt, originalOutgoingMessage);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

Stage performs no I/O and owns a copy of the complete envelope: delivery ID, route, name/schema,
JSON, optional tenant and diagnostic fields. JSONB retains JSON values, not lexical bytes.
Reader-before-writer payload compatibility remains consumer-owned. The releaser forwards opaque
payloads and cannot determine whether a receiver supports a schema or whether work remains useful.

Use a complete occurrence key once per operation context. Suppress rejects a matching tracked
occurrence, including an unsaved Stage. Release requires no tracked occurrences for its selected
scope. These rules prevent a fresh locked query from silently reusing stale tracked lifecycle
values. Business/source rows and different occurrence keys may compose in the same transaction.

## Eligibility, suppression and handoff

DueAt and release cutoff are caller-supplied UTC instants at PostgreSQL microsecond precision:
offset must be zero and ticks divisible by ten. Finer precision is rejected instead of silently
changing eligibility. A due instant survives deployment/configuration changes until an explicit
business replacement suppresses the old occurrence and stages another.

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var releasedIds = await intents.ReleaseAsync(admittedScope, explicitCutoff, maximum: 100, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
// Existing outbox dispatch executes independently after this commit.
```

Release locks at most 1..100 Pending rows with DueAt <= cutoff, ordered by due time and complete
schedule/occurrence identity. `FOR UPDATE SKIP LOCKED` permits competing releasers; it supplies
neither FIFO/fairness nor proof of an empty backlog when zero rows are returned. Native locks
last through caller commit/rollback. No expiring lease or external publication occurs here.

Suppress takes the same row lock and returns a proposal until the caller commits:

| Result | Meaning |
| --- | --- |
| Suppressed | Locked Pending is staged as Cancelled. |
| AlreadyCancelled | Retained cancellation already prevents release. |
| AlreadyReleased | Handoff has committed; this operation cannot recall it. |
| Missing | No visible occurrence matches the complete key. |

Release stages Pending to Released and the exact retained envelope in the owning outbox.
Required validation observes both participants even when detached or prematurely accepted.
An Unchanged participant must already match native stored values in the original transaction;
native key reads detect premature AcceptAllChanges and rollback to a savepoint. Genuine repeated
saves, including SaveChanges(false) followed by AcceptAllChanges, remain supported. Values and
returned delivery IDs are proposals until native commit.

The receiver still checks current source state, generation, due time and recipient/relevance
under its own concurrency protocol. Not-yet-due is distinct from stale work and may require
retrying the same delivery. Suppression cannot recall an accepted external effect or notification.

## Failure, deployment and lifetime

Operations require native PostgreSQL ReadCommitted with no ambient/enlisted transaction and
the original active native transaction/context lease. Invalid arguments use argument exceptions;
unsupported setup, tracked tampering, missing transaction and failed-operation reuse use
InvalidOperationException. Invalid retained values use InvalidDataException. Native SQL,
constraint/concurrency and cancellation failures propagate. Roll back and dispose failed state;
retry a fresh decision in a fresh context. No generic transaction/retry runtime is installed.

Rollback or process death before a release commits leaves its stored occurrence pending and
no committed handoff. Rolling back Stage leaves no new occurrence; rolled-back suppression
preserves the preceding stored state.
If commit acknowledgement is lost, dispose and inspect fresh state. A committed Released row
is not selected again. External delivery still has the outbox's ordinary at-least-once contract.

Keep Pending/Cancelled/Released evidence for the module lifetime. No pruning, expiry or ID-reuse
operation is supplied. Privileged removal ends its protection; Pending removal loses work.
Outbox retention remains a separate delivery-evidence decision after handoff.

Migration authority owns DDL and compatibility rollout. A module releaser needs schema USAGE,
SELECT/INSERT/UPDATE on occurrences and SELECT/INSERT on its outbox; it needs no occurrence
DELETE, outbox UPDATE, schema CREATE, superuser or bypass-RLS authority. These grants let a
module worker access all granted rows; scope parameters are not a database isolation mechanism.
Per-tenant operation requires consumer-deployed policies and admitted context. Native stored
verification reads must remain permitted. Do not run old writers against an incompatible schema
rename; pause/drain them for the consumer's forward migration.

## Executable consumers and limits

The test-owned [Personnel/Growth consumers](../tests/Fixtures/DueTimeIntent/README.md) adopt
these operations with different business generations and schemas. Provider tests retain actual
lock races, cancelled waits, process replacement and old/new payload readers.
[Contract tests](../tests/OutboxPostgresTests/DueIntentContractTests.cs) cover immutable
participation, repeated saves, savepoint rollback, native async reads and unsupported mappings.
The independent [NuGet consumer](../../../tools/release/consumers/DueIntents.cs) deploys a populated
forward migration, preserves earlier literal identities/payloads and runs under restricted module
credentials. Its earlier layout is consumer-owned experiment data, not two released scheduler
library versions. The initial provided protocol must remain stable for later released upgrades.

The optional [recurrence module](../Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres/README.md)
adds calendar planning over this finite mechanism. Calendar/recipient policy, supplied scheduling
workers, workflow deadlines, automatic retention and external exactly-once effects are separate capabilities. This finite
mechanism does not make source/business predicates atomic across independent module commits.
