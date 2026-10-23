# Wolverine/Marten ideas within Gloaming's library boundaries

Research date: 2026-10-23. Status: capability comparison and proposed consumer experiments;
no new interface, package, runtime behavior or extraction is selected by this document.

The owner's subsequent instruction to proceed starts the
[native Sales receipt experiment](../reports/command-receipt-experiment.md). Its implementation
and fresh proof scope are recorded separately; the comparison below retains its research baseline.

The useful question is which repeated failure protocol Gloaming could own while the consumer
keeps its native infrastructure and business authority. A transaction-bound operation can
qualify. Merely making a framework subsystem optional does not establish its value as a library.
Apply the [extraction gate](../plans/library-extraction.md) before implementation.

## Evidence and source limits

Upstream claims below come from first-party documentation fetched for this research. Marten's
current site identifies its documentation as v9.x; Wolverine pages include individually marked
6.x additions. These are descriptions of the currently documented capability, not a certified
released-package matrix. Some pages retain older migration/version wording. Recheck an exact
package/source revision before adopting an upstream implementation. No Wolverine/Marten code
was installed or executed, and documentation descriptions are not independent failure proofs.

Local comparison used the current [design](../design.md), family contracts and concrete public
interfaces. Inference/proposal paragraphs describe our judgment rather than upstream guarantees.
Gloaming's existing PostgreSQL proofs do not automatically establish any proposed mechanism.

## Broad inventory and disposition

| Documented upstream capability | Current Gloaming comparison | Disposition/inference |
| --- | --- | --- |
| Wolverine [durable local queues](https://wolverinefx.io/guide/messaging/transports/local.html), inbox/outbox and separate local handling | Durable intake, bounded processing, outbox, optional Hosting and the Wholesale local module relay already exist | Improve adoption only for a demonstrated gap; do not add another local bus |
| Wolverine [logical message deduplication](https://wolverinefx.io/guide/durability/idempotency) and [HTTP idempotency/results](https://wolverinefx.io/guide/http/deduplication) | Delivery identity, workflow start input and retained reply outcomes exist; ordinary commands and workflow signals have no general semantic receipt | Strong candidate: module-owned command receipt/result protocol |
| Wolverine [one-off scheduled delivery](https://wolverinefx.net/guide/messaging/message-bus) and [recurring messages](https://wolverinefx.io/guide/messaging/recurring) | Retry eligibility timestamps exist internally; caller-selected initial scheduling, durable timer cancellation/rescheduling and workflow deadlines are unsupported | Strong candidate: finite due-time intent, with business-state/generation checks; defer cron |
| Wolverine [partitioned sequential handling](https://wolverinefx.io/guide/messaging/partitioning) | Inbox locks a delivery; different deliveries concerning one entity can compete through native business concurrency | First prove a native per-entity concurrency recipe; no FIFO claim from serialization |
| Wolverine [saga scatter/gather and timeout patterns](https://wolverinefx.io/guide/durability/sagas.html) | Durable continuation currently permits one pending request; ordinary module-owned state can track several contributions | Consumer experiment first; do not infer a generic human-workflow or graph engine |
| Marten [subscriptions](https://martendb.io/events/subscriptions) and [async projection daemon](https://martendb.io/events/projections/async-daemon.html) | Bounded per-stream history, required current inline aggregate and explicit regeneration exist; no global feed/checkpoint protocol | Conditional later candidate; a safe feed is the hard prerequisite |
| Marten [live/inline/async snapshot reads](https://martendb.io/events/projections/read-aggregates) | Current inline aggregate is already exact at successful append; history can build separate live views | Keep this narrower model until a concrete long-stream/secondary-view requirement |
| Marten [projection versioning and rebuilds](https://martendb.io/events/projections/rebuilding) | Explicit full aggregate regeneration exists; secondary generation cutover/backfill is unsupported | Consumer-owned generation tables and cutover experiment before any engine |
| Marten [payload evolution](https://martendb.io/events/versioning), [event metadata](https://martendb.io/events/metadata.html) and [bulk event import](https://martendb.io/events/bulk-appending) | Explicit JSON aliases/upcasting and retained-message compatibility already exist; import provenance and event metadata are consumer mappings | Recipes first; historical facts, delivery records and audit entries retain separate meanings |
| Marten [natural-key lookup](https://martendb.io/events/natural-keys.html) and [dynamic consistency boundaries](https://martendb.io/events/dcb) | Native unique keys, EF queries and optimistic aggregate stamps cover the current model | Keep business-key selection native; defer tagged cross-stream concurrency machinery |
| Wolverine [PostgreSQL queue wake-ups](https://wolverinefx.io/guide/durability/postgresql) | Optional workers poll callable durable operations | Small performance candidate only after latency/idle-query measurements |
| Wolverine [command/HTTP execution pipeline](https://wolverinefx.io/introduction/what-is-wolverine) and [modular routing/storage conventions](https://wolverinefx.io/tutorials/modular-monolith) | Native endpoints/handlers, explicit module Contracts and typed module DbContexts remain consumer-owned | Do not adopt discovery, generated handler execution, automatic topology or a required root runtime |

## Candidate 1: durable logical command receipts and retained outcomes

**Upstream evidence.** Wolverine distinguishes transport-envelope identity from an
application-selected logical operation identity. Its logical claims have a separate retention
window; some paths participate in the business transaction and others use compensating release.
The documented HTTP response-replay feature is a different protocol: the response write is
outside the business transaction and process death can leave a claimed key without a replayable
response. These tradeoffs are reasons to specify our protocol, rather than copy its middleware.
Sources: [message deduplication](https://wolverinefx.io/guide/durability/idempotency),
[HTTP idempotency and response replay](https://wolverinefx.io/guide/http/deduplication).

**Already present.** [Inbox intake](../../src/Gloaming.Messaging/docs/inbox.md) compares retained
content under a delivery identity; [retention](../../src/Gloaming.Messaging/docs/inbox-retention.md)
explicitly ends that protection after deletion. [Workflows](../../src/Gloaming.Messaging/docs/workflows.md)
retain equal starts and requested reply outcomes. `SignalAsync` explicitly lacks a durable signal
receipt after an ambiguous commit. Different HTTP attempts, message deliveries or operator
requests can therefore represent the same business operation without sharing those protections.

**Proposed bounded mechanism.** In one module's database, record an admitted operation identity,
input comparison evidence and retained result/reference atomically with that command's business
changes and outgoing messages. Equal repeats return the retained result without applying changes;
different input under the same identity conflicts. This could be a small EF/PostgreSQL protocol
after executable native consumers show the recurring complexity. It need not be Messaging or HTTP
middleware and must not silently wrap every endpoint. Names and public types are undecided.

The hidden work is racing first attempts, conflict visibility/RLS, transaction abort, uncertain
commit, versioned input/result comparison, expiry and result disclosure. A permanent "claimed"
flag in a separate transaction is insufficient: a crash can strand work which never committed.
A unique constraint at commit can reject one writer, but cannot undo external effects that both
attempts performed. A long-running provider operation needs a different claim/recovery protocol.

**Consumer authority.** The module selects logical operation identity and scope, admitted tenant,
operation kind, payload meaning/canonicalization, result DTO/reference, native context and schema,
transaction boundary, permissions, retention and cleanup cadence. Re-authenticate/re-authorize
before revealing a retained result. Avoid a guessable application-global key namespace; tenant
metadata is not admission. The library would neither commit unrelated modules nor decide whether
two different import corrections are the same business operation. No default TTL can guarantee
permanent business idempotency; deletion explicitly reopens the possibility of execution.

**Smallest credible experiment.** An admitted module command changes a draft or applies one
import correction, retaining a module result in the same transaction. Send two concurrent equal
operations through independent contexts, then change the payload under the same key. Lose the
first commit response and repeat with the original operation identity. Apply the same protocol
to one authorized workflow signal only if its state/result semantics fit. Compare the native
implementation with the full proposed adoption surface before extracting.

**Required proofs.** One committed business effect/result/outbox set under concurrency; failure
before commit leaves no completed receipt; process death and ambiguous commit recover by fresh
lookup; content conflict cannot masquerade as duplicate; tenant/operation/result isolation;
old/new payload/result readers; retention/delete-versus-repeat races; cancellation, failed scope
disposal and caller-owned save/commit. No claim of exactly-once external effects or HTTP response
reproduction is required. A stored result reference can suffice if consumers can still retrieve it.

## Candidate 2: durable due-time intent and stale-occurrence rejection

**Upstream evidence.** Wolverine can defer local/external messages using durable storage or a
transport's native scheduler; an in-memory fallback has weaker restart guarantees. Recurring
messages add a separate occurrence planner, durable tracking and occurrence identity; their
documented missed-occurrence policy skips rather than backfills. Pause cannot stop an occurrence
already executing. Sources: [one-off scheduling](https://wolverinefx.net/guide/messaging/message-bus),
[recurring messages](https://wolverinefx.io/guide/messaging/recurring).

**Already present.** Gloaming [outbox](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IOutbox.cs)
accepts an immutable delivery envelope with no initial due-time argument. Native inbox/outbox rows
have `AvailableAt` for claim/retry eligibility, but this is not a supported business scheduling
interface. Their lifecycle fields are not arbitrary consumer-editable scheduler state. Workflow
deadlines, scheduled signals and durable cancellation remain explicitly deferred.

**Proposed bounded mechanism.** Persist one UTC due-time intent in the owning module transaction,
then explicitly invoke a finite operation which releases currently eligible work to the existing
delivery mechanism. Selection/claim state must make multiple callers safe. A schedule identity
plus generation can fence a replaced occurrence; the receiver also checks current business state
and generation before applying it. Whether this belongs on initial outbox enqueue or in a small
timer table depends on cancellation/reschedule/inspection requirements. Do not decide by adding
an optional timestamp to every message before exercising those requirements.

Four different concepts need separate contracts: business due time; delivery retry backoff;
execution timeout; cancellation of business work. Setting a `CancellationToken` timeout, as in
[Wolverine's execution timeout](https://wolverinefx.io/guide/handlers/timeout), does not create a
durable business deadline. A late timer is a request to reconsider state, not proof that a task
failed. If cancellation races an already published occurrence, a database status update cannot
recall it. Promise suppression only before a defined transition; after that return an honest
too-late/uncertain outcome or rely on the receiver's generation/business predicate.

**Consumer authority.** The module computes calendar/time-zone/business-day rules and UTC due
instant; owns schedule meaning, input, schema/migration, clock policy, authorization, native
transaction, missed-occurrence policy, retention and optional worker/external scheduler lifecycle.
The operation owns only the selected eligibility/transition protocol. Broker configuration,
tenant admission and business reassignment/cancellation remain explicit. Native transport
scheduling may suffice when there is no requirement for atomic module-state plus timer creation.

**Smallest credible experiment.** A module creates a reminder with a generation, then completes,
cancels or reassigns the related work before it fires. Restart before/after due time; race two
dispatchers with cancellation/reschedule; deliver an old occurrence after its replacement.
Only the currently admitted occurrence can produce the intended module effect. Keep cron,
periodic schedules, workflow graph integration and a centralized scheduling service outside the
first experiment. The stronger cancellation requirement may justify a timer table rather than
overloading outbox delivery state.

**Required proofs.** Rollback removes timer intent; no early release under the selected clock;
restart preserves due work; duplicated/uncertain release remains safe; cancel-versus-claim and
reschedule-versus-delivery races have explicit outcomes; obsolete generations cannot act;
multiple tenants/modules remain isolated; cancellation propagates without false completion;
late/missed handling and definition/payload upgrades are explicit. No hard real-time deadline,
recall of external effects or default business calendar is implied.

## Conditional candidate: checkpointed projection batches over a safe feed

**Upstream evidence.** Marten subscriptions process ordered pages through its async daemon with
progress tracking. The daemon's high-water mark deliberately separates safe committed progress
from the maximum allocated event sequence: transaction gaps and rollbacks need explicit handling.
Sources: [subscriptions](https://martendb.io/events/subscriptions),
[daemon ordering and gap handling](https://martendb.io/events/projections/async-daemon.html).

**Already present.** [EventSourcing](../../src/Gloaming.EventSourcing/docs/capabilities.md) has a
bounded per-stream reader, exact inline command aggregate and explicit full regeneration. It
does not have a commit-safe global event feed, subscriptions, secondary multi-stream projections
or a global checkpoint. Ordinary native SQL/EF joins over current module-owned state remain
adequate for many queries. A SQL view over current rows is not a persisted event projection.

**Possible bounded mechanism.** Given a proven feed protocol, atomically apply one bounded page
and advance a projection generation/checkpoint in the same owning database transaction. This
can be called by a consumer worker/CLI; it need not discover projectors, choose storage or run a
cluster. Separate modules can instead explicitly emit integration facts via existing outbox/
inbox seams and update a receiving module's view atomically with inbox completion. That approach
may remove the supposed need for a new global event-store subscription mechanism altogether.

**Consumer authority and cost.** Consumers own reducers/query shape, schema/indexes, tenant
admission, serving staleness, poison-event treatment, generation cutover, worker lifecycle and
retention. Cross-database effects cannot share a checkpoint transaction; durable handoff plus
idempotency is required. A global sequence or timestamp cursor which skips a later-committing
lower position loses events. Adding a generic checkpoint interface does not solve that problem.

**Experiment and proofs.** Choose one query unavailable from ordinary current-state composition,
then compare native SQL, inbox-driven maintained rows and a per-stream-history build. If a feed
is still necessary, prove reversed commit order, rollback gaps, page+checkpoint atomicity,
duplicate/restart behavior, tenant scope and poisoned input before extraction. A second view
generation needs population, catch-up and explicit cutover while the old one serves. Marten's
[versioned rebuild guidance](https://martendb.io/events/projections/rebuilding) is useful comparison;
even its side-effect gating documents an overlap window. Historical replay must not resend
external effects. This is a larger capability, not a small cleanup utility.

## Smaller modular-monolith opportunities: recipes before packages

- **Independent local subscribers.** Wolverine's
  [modular-monolith guidance](https://wolverinefx.io/tutorials/modular-monolith) explicitly separates
  subscriber transactions/retries and delivery identity by destination. Gloaming already has
  subscription keys and a durable local module relay. Demonstrate one integration fact reaching
  two module intake Contracts with independent failures; test partial fan-out and redelivery.
  Explicit receiving subscriptions and stable delivery identities may be enough. No automatic
  subscription registry, central module manager or general routing abstraction is established.
  The [existing relay](../../samples/Wholesale/Worker/Local/README.md) already has process-death
  proofs; its publisher mostly performs route/schema checks, contract deserialization and a
  public intake call. The difficult retained delivery/lease/transaction mechanics already live
  in existing Gloaming operations. A new relay package is justified only if a second consumer
  exposes substantial remaining mechanics; moving a switch and DTO mapping behind a bus/router
  adds configuration without removing that module-owned policy.
- **Concurrent business edits and per-entity work.** Wolverine's
  [partitioning](https://wolverinefx.io/guide/messaging/partitioning) distinguishes grouped
  serialization from stronger delivery-order guarantees under failures. Start with native EF
  concurrency tokens or a deliberate PostgreSQL lock over the module-owned row. Test two
  different inbox deliveries and foreground writes to the same business object. An in-process
  semaphore is not a cross-node guarantee, and one inbox row lock does not protect another row.
  Partitioned processing does not choose business ordering or replace version predicates.
- **Several contributions and a join.** Wolverine demonstrates fan-out/fan-in with retained saga
  state and scheduled timeout in its [saga guide](https://wolverinefx.io/guide/durability/sagas.html).
  Model the first Gloaming consumer as explicit module state plus existing inbox/outbox rather
  than forcing parallel work into the continuation runner's one-pending-request constraint.
  The consumer owns membership, completion threshold, late/duplicate replies, cancellation,
  replacement and compensation. Prove races independently before considering a reusable join
  ledger; human assignment/approval semantics do not become technical library policy.
- **Idle-worker wake-ups.** Wolverine documents optional
  [PostgreSQL LISTEN/NOTIFY](https://wolverinefx.io/guide/durability/postgresql) to reduce idle queue
  latency. PostgreSQL [NOTIFY](https://www.postgresql.org/docs/current/sql-notify.html) sends to
  listening sessions after commit, can fold repeated notifications and exposes notifications
  to database users. A native Npgsql listener could wake an existing bounded processor; durable
  rows and periodic scans must remain authoritative after disconnection/missed wake-ups. Keep
  payloads free of protected data. Measure latency/query cost, then test reconnect, missed and
  duplicate hints and shutdown before deciding whether its resource lifecycle merits a library.
- **Imports and provenance.** Marten separates [bulk import](https://martendb.io/events/bulk-appending)
  from ordinary appends and permits explicit [event metadata](https://martendb.io/events/metadata.html).
  A consumer import should retain source identity/version, correction identity and the result
  chosen by domain policy. Prefer native rows, unique constraints and the receipt experiment;
  bulk SQL must still satisfy selected tenant/audit/event-store obligations. Accepted-change
  audit is not a replayable domain event store. Immutable definition revisions do not by
  themselves require event sourcing, universal event headers or a generic import framework.
- **Payload deployment and long-running external work.** Existing codec and Earlier/Current
  proofs already cover compatible retained messages. Preserve them rather than inventing a
  second serializer. An external extraction/AI/provider call needs a separately specified
  claimed-work protocol if it must outlive process failure: short database claim, work outside
  the transaction, conditional/fenced result acceptance, duplicate-cost and reconciliation
  policy. Existing outbox completion fencing protects delivery state, not arbitrary provider
  effects, and a long provider call inside the inbox's row-lock transaction is a poor default.
  Start in the real consuming module; neither upstream execution timeouts nor existing Gloaming
  workflows prove a general job-execution or exactly-once provider protocol.

## Features not justified by this comparison

Native EF/Npgsql already supply queries, unique constraints, concurrency, transactions and
provider configuration. Do not add generic repositories, a document database abstraction,
tagged dynamic consistency boundaries, default event-sourcing for revisions, implicit shared
transactions across modules, mandatory scheduler/mediator, handler discovery or automatic
resource provisioning merely to resemble upstream systems. These may be valid designs for
those products; they do not yet remove a demonstrated repeated obligation here.

A bounded shared-connection/transaction recipe can remain an option for modules deliberately
sharing one database, but separate DbContexts do not automatically make their writes atomic.
Keep it a native consumer choice with real rollback/concurrency/isolation proofs. No promise
of separate-database atomicity, transparent extraction or framework-wide unit of work follows.

## Umbrine evidence and next selection

The source is the sibling `../umbrine` working tree inspected on 2026-10-23. Its HEAD is
`09f64c7c01f99c7b8676b9168b71b50c9ca7340f`, but its README is modified and `docs/` is untracked:
that commit is **not** a revision pin for these documents. They describe selected behavior and
open design, not implementation. No Umbrine file was changed. The source paths/headings/case IDs
below identify the owning requirements without copying the documentation into this repository.

| Current Umbrine source | Relevant requirement/status | Gloaming implication, inferred |
| --- | --- | --- |
| `docs/platform/02-personal-work.md`, Notifications and reminders; `docs/personnel/02-probation.md`, PRB-05/06 | Reminders are initial direction and stop after completion, cancellation or reassignment. Probation can extend or cancel. Timing/frequency remain open. | A durable due-time/generation mechanism has a concrete caller. The module decides relevance; an expired timer cannot decide unsuccessful probation or complete a human responsibility. |
| `docs/growth/02-reviews.md`, Regular review recurrence | The next indication is anchored to final review closure; partial/early closures reset it, cancellation does not. HR still explicitly initiates the review. Calendar-edge handling remains open. | Persist a computed occurrence, not a generic annual cron rule. The consumer owns date/time-zone/calendar policy and recalculation after changed source state. |
| `docs/platform/02-shared-conventions.md`, Drafts and validation; `docs/architecture/03-ui-and-calendar.md`, Drafts and concurrency | Resumable autosave and conflict detection; saving, submission and publication are distinct actions. | Logical command receipts could protect an uncertain accepted action. They do not merge conflicting edits, turn repeated autosaves into publication or replace native optimistic versions. |
| `docs/platform/02-data-and-content.md`, IMP-02/03 | Reimports match stable source keys; supported corrections update matched records; omissions leave other data unchanged. Exact batching/atomicity remains open. | Distinguish source-record identity from one import/correction operation. A permanent receipt keyed only by record would incorrectly suppress a legitimate later correction. Import mapping and partial-success policy remain consumer-owned. |
| `docs/growth/02-reviews.md`, Assessment stages/Returning to an earlier stage; `docs/hiring/02-v1-acceptance.md`, HIR-07; `docs/time-reporting/02-v1-acceptance.md`, TIM-17/18 | Independent contributions and business-specific completion gates, returns and reassignment. Deadline expiry supplies no missing contribution. | Use module-owned state and native concurrency first. A technical join cannot decide required assessors, visibility, excusal or approval. A returned stage needs a new attempt/generation; stale submissions/receipts cannot complete that new attempt. |
| `docs/platform/02-ai-assistance.md`; `docs/platform/02-data-and-content.md`, Inference sources; `docs/architecture/03-inference-and-data.md`, Funded inference | AI proposals require explicit human adoption; capture source revisions and funding controls. Extraction is inspectable derived content. Async execution mode and detailed recovery remain design choices. | A durable external-operation claim/result protocol may be valuable after the execution mode is chosen. Fencing can reject obsolete results; it cannot promise no duplicate provider calls/spend or grant permission to apply a result. |
| `docs/hiring/02-v1-acceptance.md`, HIR-12 | Explicit successful-hire create/link handoff into Personnel, with source access and separate target authority. | Good real caller for command receipts and a durable local-Contracts recipe if recovery requires it. The product must choose synchronous/shared-transaction or durable separate commits; sharing a process alone supplies neither atomicity nor recovery. |
| `docs/time-reporting/02-v1-acceptance.md`, TIM-21 | A valid absence correction updates an open month but proposes a separately approved adjustment for a closed month. | This is business reconciliation, not merely refreshing a read projection. Existing durable intake can drive the owning module decision. A generic projector must not bypass the period/approval rule during replay. |
| `docs/platform/02-data-and-content.md`, Historical definitions and removal; `docs/architecture/03-inference-and-data.md`, Revision storage and removal | Immutable definition revisions are pinned; adopting a new revision explicitly restarts affected work. Event sourcing is not selected just for version retention. | Keep revision/permission/removal rules in the module. Native relational revisions can suffice; Marten snapshots do not by themselves justify a new revision framework or event-sourced definitions. |

**Recommended order, as research judgment rather than an implementation queue:**

1. **Logical command receipts/results:** the clearest small capability experiment. Use one
   ordinary admitted command and one `SignalAsync` caller through the same native receipt
   protocol. A repeated old operator request arriving after the workflow has paused again must
   return its original outcome rather than act as a new signal. Keep the action's logical ID
   distinct from the workflow/entity ID and its new stage/attempt IDs. First prove this in a
   consumer; extract only the repeated transaction/concurrency/recovery complexity.
2. **Durable due-time intent:** use probation extension/cancellation and review preparation as
   two concrete timer consumers. Persist source generation, race due-time release with extension
   and cancellation, then deliver the obsolete occurrence deliberately. Test restart and late
   wake-ups. Keep recurrence calculation and notification recipients in Umbrine. Cancellation
   stops future authorized occurrences; already accepted/in-flight email cannot be recalled by
   a database flag, so the consumer must define that delivery race honestly.
3. **Broker-free fan-out recipe/proof:** one fact reaches two independently committing consumers;
   one is unavailable, then recovers without repeating the first consumer's effect. Build on
   current subscription keys/intake/dispatch. This could improve modular-monolith adoption
   without a new runtime package; do not extend Wholesale with another invented business area.
4. **External-operation attempts:** evaluate in the first actual inference/extraction worker.
   Use short native transactions around claims/results and perform provider I/O between them;
   test lost workers, lease expiry, stale source revisions, cancellation and lost provider
   responses. Funding, privacy, result adoption and any provider reconciliation remain Umbrine
   policy. Only repeated technical lease/fencing mechanics could become Gloaming later.
5. **Projection batches/feed:** keep conditional. Choose an actual slow/inadequate query first;
   a personal inbox spanning modules does not automatically require a global event feed.
   A native Contract query or inbox-maintained receiving view may already meet the need.

The first two candidates appear technically bounded and materially different from existing
Gloaming contracts. That is a research inference, not an extraction decision. A consumer-native
implementation is an appropriate result if its complexity stays small. The projection/feed
candidate has a substantially larger correctness surface and should follow an actual query need.
For a module which needs a mature event store/daemon, compare deliberately selecting Marten for
that module with implementing and supporting the missing protocol here. Consumer-owned
infrastructure permits that persistence choice; it does not require Gloaming to reproduce it
or gain a mandatory Marten dependency.
No new reusable mechanism or runtime guarantee was proven by this research.

## Provenance and verification

Gloaming comparison baseline: owner-approved checkpoint `9eaab6d`. Its nine-path snapshot
matched before checkpoint and normal hooks passed. This research changes documentation only;
the earlier runtime/package results remain dated evidence for their own checkpoints.

Umbrine working-tree SHA-256 prefixes for the central requirement sources are:

| Source under `../umbrine` | SHA-256 prefix |
| --- | --- |
| `docs/platform/02-personal-work.md` | `5af9c9a499bd` |
| `docs/platform/02-shared-conventions.md` | `27f2a3a440a8` |
| `docs/platform/02-data-and-content.md` | `2ac490706132` |
| `docs/platform/02-ai-assistance.md` | `a153702749f0` |
| `docs/architecture/03-inference-and-data.md` | `f2f6960c25b4` |
| `docs/architecture/03-ui-and-calendar.md` | `68b3d0309e63` |
| `docs/growth/02-reviews.md` | `4d1f38039b5b` |
| `docs/personnel/02-probation.md` | `9773bbd844bd` |
| `docs/hiring/02-v1-acceptance.md` | `80ccf9d21e10` |
| `docs/time-reporting/02-v1-acceptance.md` | `a77aa16b5d0d` |

The complete local manifest is `/tmp/gloaming-opportunities-umbrine-sources.json`; it records
15 selected source files; all hashes still match after research. Fresh repository checks passed:
TypeScript types, tooling formatting, all six reference-checker regression cases and the reference
scan (860 local Markdown targets and 296 literal project/source targets, zero broken references).
Whitespace checks pass. Runtime/source/project/fixture inputs have no diff from `9eaab6d`.
No new library tests or native runtime proofs are claimed for these unimplemented proposals.
The research file/index and checkpoint status remain unstaged for owner review; the existing
index is preserved. No later checkpoint, package publication, Umbrine edit or runtime feature
is authorized merely by this comparison.
