# PostgreSQL recurring intents

An optional bounded recurrence module over
[finite due intents](../docs/due-intents.md). It concentrates calendar evaluation, retained
generation/progress, stable occurrence identities and atomic finite-intent staging. It selects
Cronos 0.13.0 and the PostgreSQL messaging package. Finite scheduling remains independently usable.
There is no worker or implicit clock, transaction, schema migration, save or commit.

Import `Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres` for recurrence types and
`Gloaming.Messaging.EntityFrameworkCore.Postgres` for finite/outbox native mapping. Shared save
composition uses `Gloaming.EntityFrameworkCore`; native context/migration ownership remains visible.

## Composition

The owning native context configures three independent tables and selects required save guards:

```csharp
modelBuilder.ConfigurePostgresOutbox("maintenance", "outbox");
modelBuilder.ConfigurePostgresDueIntents("maintenance");
modelBuilder.ConfigurePostgresRecurrence("maintenance");

services.AddPostgresRecurrence<MaintenanceDatabase>();
// On this scoped context's options:
options.ConfigureGloamingCapabilities<MaintenanceDatabase>(serviceProvider);
```

Manual integration calls `ValidateRecurrenceChanges()` before native synchronous saves and
awaits `ValidateRecurrenceChangesAsync(token)` before native asynchronous saves. These include
finite-intent/outbox validation. Guard both `SaveChanges(bool)` overload paths. Consumer-owned
migrations deploy the fixed model before writers start; never migrate automatically at runtime.

Each generation has the complete ordinal `(ScopeKey, ScheduleId, GenerationId)` key, exact text
definition/progress, positive bigint Revision concurrency token and integer State. Default table
is `recurring_intents`. Retain the mapped columns, key, ordinal scope, no filters/conversions or
generated properties and independent table. Schema/table identifiers are quoted and limited to
63 UTF-8 bytes. Scope is at most 256 .NET characters and independent of optional envelope tenancy.
The module worker needs schema USAGE and SELECT/INSERT/UPDATE on recurrence and finite tables,
plus SELECT/INSERT on outbox. Migration/retention authority remains separate; tenant RLS is not
implied. Do not configure query filters on these technical records.

Use a fresh context and its original explicit native ReadCommitted transaction for each decision.
Use each complete generation once in that context. Business rows and other complete generations
may participate. Register is a no-I/O proposal, then save and commit explicitly:

```csharp
var key = new RecurringIntentKey(scope, scheduleId, generationId);
var definition = RecurrenceDefinition.Every(
    "storage-rotation-v1", anchorUtc, TimeSpan.FromMinutes(5), MissedOccurrences.CatchUp(20));

await using var transaction = await database.Database.BeginTransactionAsync(token);
new PostgresRecurringIntents<MaintenanceDatabase>(database).Register(key, definition);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

A later fresh transaction invokes `PlanAsync(key, definition, observedAtUtc, maximumExamined,
envelopeFactory, token)`, then saves and commits its proposals. The factory receives MessageId,
OccurrenceId and nominal DueAt; construct an `OutgoingMessage` with that MessageId. The factory
must be pure: it must not perform I/O, send messages, mutate tracked rows or make another planning
call. Select payload/route/tenant/recipient policy explicitly. The resulting full envelope becomes
immutable finite work. A separate selected caller releases finite work into the outbox.

Register and planning are separate transactions: registration retains the generation but arms no
occurrence. Call Plan at the selected initial observation to arm its first occurrence. The anchor
is exclusive. Interval schedules measure elapsed UTC; a local calendar uses `RecurrenceDefinition.Cron`
with explicit five/six-field syntax, `TimeZoneInfo`, rules version and missed policy. No ambient
local zone or wall clock is read. UTC instants/intervals require microsecond precision. Cronos
forward semantics adjust spring gaps, run daily autumn times once and preserve interval occurrences
across both autumn offsets; see the pinned [Cronos DST contract](https://github.com/HangfireIO/Cronos/blob/v0.13.0/README.md#daylight-saving-time).
Business holidays, calendars, anchors, recipients and relevance remain consumer-owned.

## Bounded planning and clocks

Planning locks the generation row until commit. Competing planners subsequently observe committed
progress; different complete generations do not share that lock. Native lock waits honor cancellation.
Rows/identities/counts returned are proposals until commit. The native transaction atomically stores
progress and finite occurrences. Required guards reject omitted, detached, prematurely accepted,
modified or savepoint-reverted participants. Genuine successful saves and explicit native acceptance
are supported; a failed operation requires disposal and a fresh context/transaction.

- `Skip` omits unplanned occurrences at or before the explicit observation and arms one future
  occurrence. It does not recall previously armed work that later became overdue.
- `Coalesce` scans forward through a captured cutoff, retaining its scan position and candidate
  across commits. It emits only the final latest unplanned occurrence through that cutoff. Newer
  observations cannot move an in-progress cutoff. No partial candidate is emitted. A new window
  handles later backlog. No reverse cron lookup is used.
- `CatchUp(maximum)` emits the earliest unplanned occurrences, with an explicit bound of 1..100
  finite stages per call. The bound includes any newly armed future occurrence. Remaining work
  returns MoreWork; call again with a fresh context. A work budget may end a batch earlier.

`maximumExamined` is 1..4096 forward elapsed-candidate steps per call. Coalesce stages at most its
one elapsed candidate plus one future occurrence. Repeated early polls retain that single armed
future occurrence; they do not walk indefinitely into the future. MoreWork requests another bounded
decision; it makes no fairness or delay promise. Exact skipped counts are not supplied. When no
future occurrence exists the generation can remain Active with no additional work.

The highest observed instant is retained. A later observation below it fails closed; wait for time
to catch up or deliberately stop/select a new generation. Already staged nominal UTC instants and
their delivery/occurrence identities never change. Identities use a versioned SHA-256 derivation
of the complete scope/schedule/generation/purpose/nominal instant; unique native keys retain duplicate
evidence. Do not reinterpret them as sortable IDs or regenerate them after evidence removal.

## Calendar compatibility, cancellation and deployment

Definitions retain consumer version, anchor, expression/syntax or interval, missed policy/bound,
zone ID, caller-attested rule-data version, actual immutable zone object's serialized-text hash,
native runtime version and Cronos informational version. Planning requires an exact match before
progress changes. This intentionally refuses runtime/evaluator/rules upgrades, including conservative
serialization differences. Pin deployment inputs and explicitly replace a generation after review.
There is no automatic "compatible" upgrade or silent recomputation of committed UTC occurrences.

The serialized-zone hash is a conservative mismatch check, **not** a portable complete semantic
fingerprint or a restorable frozen rules snapshot. The consumer verifies/pins the actual time-zone
data source loaded by its host and passes its immutable artifact identity as `rulesVersion`.
Include configured TZDIR or the platform's actual source in that attestation. The library does not
modify process-wide zone settings, load arbitrary rule files or infer a trusted tzdata version.
Cross-platform/runtime moves require an explicit deployment decision. The native fixture replaces
an actual isolated host's TZif data and proves refusal before stored progress advances.

`StopAsync(key, maximum, token)` locks the generation, disables planning and suppresses at most
1..100 pending finite occurrences in the same transaction. Repeat fresh decisions until MoreWork
is false. Stopping prevents new planning even while suppression remains. A stopped generation
cannot restart. Stop does not require recreating its old calendar definition. Released finite work
is irrevocable; the receiver must check the consumer's current generation and business relevance.
Stopping and release races preserve the committed native outcome, never external recall.

The library retains separate generations; it does not choose one "current" business generation.
The consumer locks/updates its own relevance row and stops an old generation when replacing it.
Only register a replacement according to that policy. Registering another key alone does not stop
the old one. A retained complete key cannot be reused. Retain definitions, progress and finite
identity evidence for the module lifetime. Authorized explicit removal ends those protections and
requires a receiver/deployment plan; no automatic pruning is supplied.

On native save/commit, cancellation, callback or validation failure, dispose the entire operation.
A lost commit response may already mean success: inspect retained generation/progress/finite IDs
in fresh state. Do not replay an old proposal or blindly reuse its context. Business registration
uncertainty needs its own logical action receipt if the caller cannot recover its original key.
No exactly-once external effects follow from stable occurrence identity.

Deploy tables/grants and compatible readers first, then selected writers/planners. Existing finite
writers remain valid and do not acquire recurrence semantics. One initial provided format is
supported; future format changes require retained fixtures and actual old/new binary rollout proofs.
The [native fixtures](../tests/Fixtures/Recurrence/README.md), provider tests and
[package-only adopter](../../../tools/release/consumers/Recurrence.cs) exercise different obligations.
