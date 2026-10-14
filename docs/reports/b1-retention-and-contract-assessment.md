# B1: retention and public-contract assessment

Date: 2026-10-14. Status: closed by owner direction as a documentation-only assessment.
Recorded retention, ordering and integration-contract constraints are the slice outcome.
No replacement implementation or library interface is selected. The staged feed experiment
remains intact as evidence; adoption is deferred until a concrete consumer requirement arises
in a later slice. Closure does not approve committing or merging the experiment.
The owner subsequently requested excluding experiment files from the common Wholesale
sample. They are preserved in a [separate source/patch snapshot](../../experiments/b1-retained-profile-feed/README.md).
The active sample has no feed dependency; the existing staged review snapshot is unchanged.

## What the current code actually supports

- [OutgoingMessage](../../src/Rootbolt.Messaging/Rootbolt.Messaging/OutgoingMessage.cs)
  supplies delivery identity, wire name/schema and consumer-selected JSON. It does not
  supply an aggregate revision, publication offset or historical replay contract.
- [OutboxMessageRecord](../../src/Rootbolt.Messaging/Rootbolt.Messaging.EntityFrameworkCore/OutboxMessageRecord.cs)
  retains the original payload and delivery lifecycle. The
  [dispatcher](../../src/Rootbolt.Messaging/Rootbolt.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs)
  marks accepted rows dispatched rather than deleting them. There is no implemented
  cleanup/replay API or defined history availability window. QueuedAt is insertion time;
  DispatchedAt is recorded transport acceptance, not source commit order.
- [Sales profile writes](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs)
  explicitly advance CustomerRow.Version and update customer/address atomically. That
  supports this operation's profile revision; it is not a universal integration contract.
- [StockIssueRecordedV1](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/StockIssueRecordedV1.cs)
  includes a stock-position stream version, issue quantity and remaining quantity.
  [StockIssueMessages](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessages.cs)
  deliberately maps a selected decision to a public message. Openings and receipts do
  not all publish corresponding messages. Stream versions therefore need not be contiguous
  in the integration history, and replaying this effect-bearing contract differs from
  replacing a current-state record.
- [D1](../plans/remaining-capability-roadmap.md#work-queue) is a proposed DDD-pattern
  assessment. It does not promise persistent domain events, automatic public mapping or
  a streaming runtime. Existing event sourcing retains private per-stream facts; ordinary
  state-stored DDD need not retain domain events at all.

## Retention and reconstruction are different requirements

Exact historical replay requires retained public messages, or retained facts sufficient
for an explicitly defined historical derivation. Current state alone cannot recover an
old price change or a past business notification. A producer can still export its current
public state or answer a lazy lookup without preserving all changes. That is state transfer,
not historical replay. Catching concurrent changes around that transfer is a further contract.

Domain events, event-sourcing facts and integration messages have different audiences and
lifecycles. Microsoft distinguishes local domain reactions from asynchronous propagation
of committed changes.
[Microsoft domain/integration guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation).
A one-to-one mapping can be valid when deliberately selected; automatic mapping is not
justified by shared event terminology. Several facts can produce one public message, one
fact can produce several, and many private facts should produce none. Applying today's
mapper to old facts does not establish that it reproduces the original public messages.
Reconstruction must not accidentally run ordinary handlers and repeat business effects.

## Storage alternatives, with no selection yet

| Source | What it can offer | Additional contract needed |
| --- | --- | --- |
| Existing retained broker log | Replay of published public records still available there | Routes/keys, partition progress, schema compatibility, retention and expiration; publication lag remains separate from source state |
| Retained outbox payloads | Original selected messages that were enqueued and preserved | Privileged receiver-specific replay/export, bounded fixed selection or proven ongoing progress, availability floor, identity and effect policy; preserve ordinary dispatch state |
| Producer export or targeted lookup | Current disclosed state, including records that never emitted messages | Coherent source read and consumer freshness/readiness; overlap or reconciliation if ongoing updates must converge |
| Private event history | Inputs for rebuilding a producer-owned public representation where history suffices | Explicit derivation and compatibility; retained private facts are not automatically public messages or a multi-stream cursor |
| Custom public feed | Precisely the progress and retention semantics implemented for that feed | Added producer storage/write coupling, paging/expiry and operational costs; the B1 gate is one particular trade-off |

Retaining outbox rows can be a pragmatic reuse of storage. It does not require building
Kafka, but adding an ongoing replay service is more than extending a retention duration.
A one-time export of a fixed visible set has a smaller obligation than continuously polling
by time. The set must remain available for the export/restart window, and its incompleteness
for state bootstrap must be explicit. No such export is implemented here.

“Send everything before datetime T” can be a useful operator selection, but is not a safe
ongoing checkpoint: a row stamped before T can commit after selection. A timestamp plus
MessageId breaks equal-time ties but still does not account for late commits. A sequence
allocated before commit has the same hazard. PostgreSQL's sequence calls do not serialize
the surrounding transactions and rollback can leave gaps.
[PostgreSQL sequence semantics](https://www.postgresql.org/docs/current/functions-sequence.html).
An overlap window alone cannot guarantee completeness with unbounded transaction/publication
delay. A fixed captured set, reconciliation or an explicitly proven progress boundary is needed
for the corresponding claim. Replaying only dispatched rows has another delayed-publication
boundary; it cannot silently stand for all committed outgoing messages.

Original MessageId usually stays stable for redelivery of the same message. A receiver
that already processed it may correctly ignore it. A new projection's receipt scope and
permissions must be explicit; resetting normal dispatch flags or broadcasting old workflow
requests can repeat unrelated effects. Replay targets and identity policy belong to the
chosen consumer, not to a generic “resend everything” operation.

## Revisions are optional, scoped contract semantics

SchemaVersion selects the decoder. MessageId identifies a delivery. A retained log cursor
tracks records read. A source-defined public revision can order disclosed state for one
key. None is a substitute for the others; timestamp metadata supplies no general ordering.

The previously researched snapshot-V merge is conditional, not a capability of today's
envelope. It requires coherent state/revision reads and updates with compatible semantics.
Complete replacements can reject stale revisions; partial patches require a valid base or
refresh. Business-effect messages require their own idempotency and causal rules. One message
spanning several aggregates may have no meaningful single aggregate revision. A producer-owned
public view could define its own revision if required, but that is additional maintained state,
not something inferred by the messaging library.

## Closed outcome and evidence

The owner accepted recording these facts and closing B1 without further implementation.
This assessment and current-status reconciliation are complete. No streaming extension,
default domain-event retention, outbox lifecycle change or generic revision interface was
selected. B2 snapshot/bootstrap implementation remains outside this slice.

Before selecting any implementation, specify one producer/receiver operation, whether the
receiver needs current state or past effects, and what it may do when data is missing or stale.
Then select the smallest storage/transfer contract that satisfies that requirement. A sparse
lookup may be sufficient; a complete export may be needed; historical effects require history.
The [bootstrap research](integration-event-bootstrap-research.md) and
[sparse-consumer examples](sparse-consumer-hydration-research.md) supply alternatives, not an
automatic build plan. No real receiver requirement has yet justified the Sales writer gate.

The staged [B1 proofs](b1-replayable-integration-feed.md) remain dated evidence for that specific
retained feed: late commits, gaps, paging, duplicates, expiry and restart. Its diagnostic journal
does not demonstrate business adoption or a generally safe versioned snapshot merge. Runtime
code, migrations, archive and index were preserved at the initial assessment handoff. That
handoff changed only documentation; no runtime tests were rerun and **no new reusable mechanism
was proven**. No library interface,
template preset, retention engine or replacement sample was introduced. Adopting or removing
the existing experiment requires a separate concrete scope. The owner subsequently authorized
the isolation described below, preserving the findings without adopting the experiment.

Documentation verification: local links in the changed documents resolve and `git diff --check`
passes. The staged snapshot remains byte-for-byte unchanged. Revisit these findings only when
a later slice supplies a concrete receiver need; no immediate follow-up is required for B1 closure.

## Experiment isolation

The experiment's source, tests, native migration and modified integration files were preserved
byte-for-byte with hashes and a reconstruction patch against merged WF2 at `d674e39`.
Normal Sales profile writes, mapping, registration, Contracts, HTTP setup and concurrency
proofs were restored to that merged baseline. The unmerged feed migration and model additions
exist only in the experiment snapshot; earlier migrations and archived fixtures are unchanged.
Active solution/architecture references and the feed CI step were removed. Snapshot files are
excluded from normal formatting and CI selection. They are historical evidence, not a standalone
maintained executable sample. The current staging snapshot remains unchanged; all isolation
changes are unstaged. Fresh isolation verification is recorded below, separately from the
original feed executions. No new reusable mechanism was proven.

Fresh verification on 2026-10-14 for this isolation:

- Active solution build passed with zero warnings/errors.
- Real PostgreSQL HTTP proofs: 105 passed, zero failed/skipped; persistence/migration proofs:
  36 passed, zero failed/skipped. Active architecture checks: 82 passed, zero failed/skipped.
- CSharpier checked 625 active files. Style/analyzer verification and `git diff --check`
  passed. Changed-document local links resolve; snapshot documentation retains original paths.
- All 34 preserved files match their manifest hashes. Applying the stored patch to baseline
  files in a disposable directory reconstructs every preserved file exactly. This is a file
  reconstruction proof, not a fresh execution of the original feed tests.
- Every affected active runtime/test/solution/workflow file matches merged WF2 or is absent;
  no feed API, gate, model mapping, activation or active migration remains. All 800 archived
  originals and existing migration bytes are preserved. The staged snapshot is unchanged.

No feed test, broker, Aspire/browser topology or unrelated family suite was rerun for isolation.
