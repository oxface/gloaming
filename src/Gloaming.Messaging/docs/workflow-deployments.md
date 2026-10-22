# Deploying retained workflow instances

The runner loads the one configured definition for a binding. Application release number and
persisted `DefinitionVersion` are different identities. A compatible application deployment can
keep version 1 while changing logging, optional state or implementation details that preserve
the meaning of retained progress. A version change rejects old instances; it does not migrate,
route between definitions or replay Start.

The [independent executable consumer](../tests/Fixtures/WorkflowDeployment/README.md) proves an
earlier binary's waiting instance and admitted delayed reply continuing under a separately
compiled Current binary. It also proves an earlier `AwaitingInput` instance resuming its stored
signal. Both builds use ordinary native transactions and existing inbox/outbox composition.

## Compatibility obligations

Review the complete retained contract before releasing:

- Keep workflow type, definition version and every still-reachable continuation name. Renaming
  a C# method is harmless if its persisted binding name and semantics remain compatible.
- Decode original initial input and state with the consumer's JSON settings. New optional fields
  need valid defaults for old JSON; new required fields, meaning changes, enum renumbering and
  tightened validation need their own explicit treatment. Equal starts compare retained input
  JSON, so adding serialized defaults to the *input* can break restart identity even if state
  remains readable. The example changes only state.
- Preserve accepted reply aliases/schemas and producer/business validation for pending commands.
  Upcasting an admitted envelope in place would also change retained duplicate identity. Queued
  [message compatibility](schema-compatibility.md) remains its own contract.
- Preserve pending operation IDs, correlation/causation and retained outcomes. Preserve signal
  bindings and any state needed after long human/operator pauses. New code must honor effects
  already requested or accepted by old code, including compensation obligations.

A matching version is a consumer compatibility assertion. The library cannot detect a meaning
change, renamed binding or changed reply schema in advance. `ReadAsync` can succeed for the latter
two and the delayed reply can still fail. The process tests prove both failures leave the process,
outbox and original inbox envelope unchanged while inbox failure accounting commits separately.
Deserialization, business validation and missing required fields can fail too. Read probes alone
are therefore insufficient; exercise representative retained continuations/signals in a restored
or disposable database with literal historical inputs and JSON.

## Practical compatible rollout and recovery

1. Inventory persisted workflow types/versions, active continuations, `AwaitingInput`, pending
   outgoing operations and retained incoming replies. Include completed rows while they remain
   available for reads, duplicate starts or late outcomes. Preserve backups and original JSON;
   JSONB stores JSON values, not lexical formatting.
2. Run the candidate against representative waiting, paused, completed and refused historical
   instances. Check late/duplicate outcomes, required fields and rollback compatibility. Review
   actual business semantics and external effects separately from the technical probes.
3. Keep the compatible persisted definition in the candidate. Coordinate schema/producer/receiver
   deployment and stop the old workers before switching this proof topology. Install the candidate,
   then let existing inbox/outbox workers handle retained work. The example establishes a stopped
   old process followed by a new process; simultaneous mixed-release workers are not proven here.
4. Observe workflow versions/statuses and inbox failures. On incompatibility, stop candidate
   processing and restore compatible code. Retry valid retained replies through existing inbox
   eligibility or authorized [inbox recovery](inbox-recovery.md). The example uses a short consumer
   retry delay and proves fresh-process recovery without rewriting any original envelope or state.
5. After uncertain commit or process death, inspect committed progress. Let reply deduplication
   and retained outcome matching decide repeated deliveries. A killed save before commit rolls
   back; an acknowledged commit may have survived an unavailable response. Operator signals need
   an explicit read-before-retry decision because no signal receipt is supplied.

This rollout does not require a migration engine. The proof adds one optional annotation which
the earlier binary can read by ignoring unknown fields; an earlier write can drop that annotation.
That is acceptable only because the consumer declares it optional and non-authoritative. It does
not establish arbitrary rollback compatibility. Invalid original business input is an operator
case; reinstalling compatible code cannot invent its missing value.

## Keeping compatibility versus draining

| Choice | Useful when | Consumer obligations and limits |
| --- | --- | --- |
| Keep the compatible definition in the new release | Long-lived waits, delayed replies and operator pauses must continue | Retain old state decoding, bindings/wire contracts and business meaning; test retained instances. This is the selected executable journey. |
| Drain before removing incompatible behavior | Work is bounded and admission can actually pause | Stop new starts/signals and relevant producers; keep old processing/transport alive until active workflows, required operations and incoming replies settle. Handle paused/failed workflows explicitly; missing replies are not failures. Prove queue/in-flight drain in the real transport topology. |
| Explicit consumer migration | A real business change cannot retain compatibility or drain | Separately design state/ledger/input/wire transformations, concurrency/admission, transactional audit, repeatability and rollback. No such migration is implemented or implied by this proof. |

The example's `drain` counts `Status != Completed` and unprocessed replies. It rejects both waiting
and `AwaitingInput`; it grants no lock, checks no broker/external receiver and does not consume its
retained outbox. Zero local counts are not a complete deployment gate. Quiescence is a consumer
procedure, not a property of a SELECT.

Even after active instances drain, retained **completed** version-1 rows still reject a version-2
typed read (proved). Duplicate starts and old outcomes still need the retained definition. A
breaking release therefore needs a deliberate retained-history/late-delivery strategy, such as
keeping compatible processing/inspection available or introducing a new consumer-owned workflow
type for new semantics with explicit routing. Neither blanket deletion nor changing
`DefinitionVersion` with SQL is a safe general answer. No automatic old/new routing, migration,
deployment coordinator, version registry, retention policy or compatibility certification is added.
