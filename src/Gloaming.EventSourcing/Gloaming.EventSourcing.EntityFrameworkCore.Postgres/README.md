# Committed event feed and secondary projections

This optional PostgreSQL18 module captures immutable source rows and explicitly executes bounded
secondary projection turns. It references native Npgsql EF Core and DI abstractions only. Existing
provider-free event sourcing, required inline state and ordinary stream reads remain independent.

## Installation and write cost

The consumer owns schemas, migrations, roles and source journal constraints. Create a trusted feed
schema and select a permanent `EventFeedLocation(schema, feedId)`. Pass fixed `EventFeedSource`
bindings to `EventFeedInstallation.Create`, review the returned SQL and execute the complete script
in one native migration transaction. Pause source writers for rollout; installation locks every
source table in schema/table ordinal order before reading history or installing triggers. An earlier
in-flight writer finishes before the migration proceeds. Existing binaries need no feed registration:
the database trigger captures their subsequent inserts too. Startup never installs SQL.

Supported sources are ordinary append-only tables without inheritance, partitioning, RLS or rewrite
rules. Explicit source/column/schema identifiers use ASCII letters/digits/underscores, starting with
a letter/underscore, at most63 bytes. One to32 distinct source bindings each select one to8 complete
stream-key columns, a positive native integer version and the **complete native primary key in its
declared order**. Keys are nonnullable native text/UUID/integer values. Baseline history must begin
at1 without per-stream gaps/duplicates. Producers retain responsibility for valid live stream order
and constraints. The feed copies facts; it is not a stream concurrency or domain validation engine.

Each complete stream-key/event-key JSONB array must satisfy native `pg_column_size <= 512`.
Returned cursors allow up to16,384 encoded characters, including worst-case key escaping.

Existing history becomes one **synthetic baseline**. Original historical cross-stream commit order
cannot be recovered. Thereafter, the first captured insert updates an ordinary transactional head
row and retains that native write lock until transaction end. All further inserts in the transaction
reuse its position. A competing writer waits; rollback/savepoint rollback removes the associated
counter and copies together. Transaction identity groups work and never orders it. **A slow or
prepared transaction stalls every writer selected into this feed.** Measure that cost under actual
load. Native deadlocks, serialization errors, cancellation and statement timeouts remain producer
failures requiring fresh consumer decisions. No sequence/timestamp completeness watermark is used.

The feed stores the entire native source row as JSONB. Stream/event keys are copied as JSONB arrays;
their persisted canonical JSON text uses `C` collation for ordering and uniqueness, independently
of database locale. Within a position, order is source, complete stream-key text, numeric stream
version, complete event-key text. This is a technical publication order; numeric key components
are ordered as encoded text. Source UPDATE/DELETE/TRUNCATE are refused by installed triggers.

## Runtime grants and retention

Install using a dedicated nonsuperuser migration owner. Capture functions are `SECURITY DEFINER`
with fixed `pg_catalog, pg_temp` search path, fully qualified owned objects, no dynamic SQL and no
PUBLIC execute. Runtime source writers need ordinary source INSERT grants, no feed-table writes
or function ownership. Do not grant runtime schema CREATE, trigger/function replacement, trigger
disable, source mutation or feed retention rights. These credentials and trusted schema ownership
are part of the contract, not an authorization model supplied by copied tenant keys.

Projection credentials need source-independent feed head/entries SELECT, checkpoint SELECT/INSERT/
UPDATE, pointer SELECT/INSERT/UPDATE and their selected output rights. Activation additionally needs
`UPDATE(singleton)` on `feed_head` for native `FOR UPDATE`; grant no update rights on position,
identity, protocol or transaction columns. The singleton check permits only `true`. Projection
credentials can change technical progress and must be trusted for that role. Runtime readers need
only selected output/pointer SELECT. This first source protocol provides no RLS certification.

Retain source facts, feed copies, feed identity and source bindings for feed lifetime. No online
source-set expansion, prefix trimming, automatic expiry or feed removal is supported. Changed
bindings/key encodings or PostgreSQL major upgrades require a reviewed compatibility migration or
a new feed identity and controlled bootstrap. Keep owned migrations and retained payload fixtures;
the initial protocol is not evidence of two released library versions.

## Explicit reads and turns

```csharp
// The factory establishes required operation context before resolving ReportingDatabase.
var feed = new PostgresEventFeed<ReportingDatabase>(scopeFactory, location);
var limits = new FeedLimits(128, 1_048_576);
var page = await feed.ReadAsync(FeedCursor.Start(location.FeedId), limits, token);
// Persist page.Next.Value only after the consuming operation has accepted this page.

var generation = new ProjectionGeneration<ReportingDatabase>(
    "balances", "2026-v2", "balances-reader-v2",
    async (db, selectedGeneration, facts, cancellation) =>
    {
        // Decode consumer-owned aliases/schema/body, select complete ownership keys,
        // and stage only rows qualified by selectedGeneration. No SaveChanges here.
        await ApplyBalancesAsync(db, selectedGeneration, facts.Events, cancellation);
    });
var turn = await feed.ProjectNextAsync(generation, limits, token);
```

`ReadAsync` uses one statement snapshot of committed head and entries. A cursor contains the full
ordering tuple or an end-of-head fence and the immutable feed identity. `Parse` restores returned
values; accepting arbitrary caller-chosen progress can skip facts and grants no authority. Reads
bound returned count1..4096 and native UTF-8 JSON envelope bytes1..16MiB with one metadata lookahead.
An oversized first event throws `FeedEventTooLargeException` with its retained identity/required
size and no progress. Key/protocol metadata, database CPU/memory and callback duration are separate
from that output bound. Native JSON parsing supports the default System.Text.Json depth64.
A page can split one original source transaction; projection atomicity belongs to the selected page.

Every projection turn owns a fresh scope/context and native ReadCommitted transaction. Ambient
transactions, multiplexing, already open connections and pretracked/enlisted/current-transaction
contexts are unsupported. Factories must return a context whose native connection is closed. The first
turn creates progress at Start. The consumer must provide an empty generation-qualified target
and never reuse a generation name. A retained different definition identity fails before callback;
the identity is a consumer compatibility assertion, not a code hash or proof of business meaning.

A cooperative transaction advisory lock excludes competing turns for the same feed/projection/
generation; a hash collision can cause extra `Busy` results. There is no FIFO. The callback stages
selected output changes; the operation saves them, advances the exact checkpoint and commits both
together. It may run again after rollback. Callbacks must not save, clear/disconnect staged changes,
replace/commit transactions, append source facts, mutate technical progress, independently commit
or perform external effects. Ordinary EF saves during callback are rejected and transaction escape
is detected afterward. The module cannot undo effects from unsupported callback behavior or enforce
arbitrary consumer output schemas. Consumer reducers/readers own aliases, upcasting and admission.

The owned save detects pending/suppressed writes and verifies each selected row/deletion against
native stored values before checkpoint advancement. Selected scalar values must survive the save
composition; native store-generated values may be assigned during save. This adds native reads per
selected output row. Projection credentials require output SELECT as well as selected write rights.
That SELECT must have unfiltered native visibility for every selected output key; output RLS or
other security filtering is unsupported. A hidden row cannot serve as evidence that a selected
deletion reached storage. Consumers own and review these native visibility/grant assumptions.
Reducers must bound their own fan-out; event-page limits do not bound how many output rows consumer
code stages. Other save preparers may add effects but must preserve these projected values.

Selected output supports ordinary single-table EF entities with primitive numeric/boolean/character
values, enums, strings, Guid, decimal, native date/time values, byte arrays and JsonElement. Bytea
snapshots are independent and compare by content. Insert defaults exempt only unspecified/temporary
or ignored values; explicit values and existing-row updates remain protected. Identity keys and
computed columns receive their operation-specific native generation rules. Owned/complex/inherited/
split/shared or alternate view/query/function read mappings, arbitrary CLR objects, other arrays and conditional update-generation modes
are rejected before the owned save. Consumers may use simpler scalar outputs or prove a separate
adapter rather than treating an unsupported mapping as successful progress.

Poison input, native constraint/checkpoint/save failures or cancellation stop progress and dispose
the scope. No skip/dead-letter cursor or automatic retry exists. Once commit starts, missing
acknowledgment returns `Indeterminate`; a fresh turn reads retained progress to determine whether
the prior page committed. Never automatically repeat external work. An acknowledged result stays
known if cleanup fails; `Error` retains cleanup diagnostics. Check both result status and error.
Read-only native failures remain exceptions. Hosts own polling, supervision and retry presentation.

## Backfill and visible generation

Build another generation from Start while the current output remains readable. Every output key
includes projection generation and complete ownership keys. `ActivateAsync(name, candidate,
expectedActiveGeneration)` excludes that candidate's projector, fences source writers using the
head row and checks that no retained fact remains after its checkpoint. A lagging candidate returns
`NotCaughtUp`; a stale expected pointer returns `Conflict`. An already active candidate returns
`AlreadyActive` even after later appends. Successful pointer commit returns `Activated`; missing
commit acknowledgment is `Indeterminate` and can be resolved by a fresh pointer observation/retry.

Readers join the pointer and generation-qualified output in **one SQL statement**, or use an
explicit stable snapshot for both. Separate pointer/read queries can race cutover. Appends after
activation introduce ordinary eventual lag. Continued old workers can change their old rows only;
they never select the visible generation. This depends on cooperative reducers qualifying outputs.
Generationless old readers need a consumer-owned reader migration or pause. Retain old outputs
until their readers/workers drain; remove an inactive pointer/checkpoint/output only through reviewed
consumer maintenance after draining workers. No automated retention or traffic orchestration exists.

The [native consumers and process hosts](../tests/Fixtures/EventFeed/README.md) demonstrate two
different reducer/key shapes and independently compiled earlier source writes. This is secondary
eventual state; it does not weaken the required exact-head inline command aggregate.
