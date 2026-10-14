# Integration events and new-consumer bootstrap research

Research date: 2026-10-13. Scope: selective public integration events, lazy reference
loading, export/import and retained broker state. This report proposes alternatives;
it authorizes no implementation or change to the staged B1 work.
Follow-up, 2026-10-14: B1 was closed as an assessment and its experiment was
[isolated outside Wholesale](../../experiments/b1-retained-profile-feed/README.md).
Descriptions of its implementation below are dated research context.

## What problem needs solving?

Ordinary integration events keep existing subscribers informed of committed changes
that the producer chooses to disclose. Microsoft distinguishes these asynchronous
cross-service messages from in-process domain events. They must represent successfully
persisted changes. Nothing here requires exposing every private mutation.
[Microsoft integration-event guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation).

A second, optional requirement is giving a new service data that existed before its
subscription. A product that was created years ago and never changes again supplies
the simplest counterexample: subscribing today cannot reveal it. This is a bootstrap
requirement only if the consumer needs that existing data. A workflow interested only
in future orders might need no bootstrap at all.

The current [B1 consumer](../../experiments/b1-retained-profile-feed/README.md)
already selects accepted Sales profile edits and retains a public full-state V1
payload. It is not generic database change capture. B1 added a direct-pull retained
log, bounded paging and source progress; it did not add broker publication or initial
population. Pre-existing profiles have no synthetic entry. Its Organization writer
gate solves safe progress in that particular log. It does not establish that an
ordinary integration-event architecture needs the same gate.

## Contract semantics come before transport

Fowler distinguishes a small change notification, whose receiver may fetch details,
from event-carried state transfer, whose receiver has enough data to work locally.
Both are distinct from event sourcing. His comparison identifies the trade: calls
back to the producer versus receiver-maintained copies, with improved availability
and reduced query load for the latter.
[Fowler's first-party pattern explanation](https://martinfowler.com/articles/201701-event-driven.html).

An illustrative producer-owned contract could be:

```text
ProductCatalogEntryChangedV1
  EventId, TenantId, ProductId, CatalogRevision
  Sku, DisplayName, PublicPrice, Currency, Availability
  OccurredAt
ProductCatalogEntryRemovedV1
  EventId, TenantId, ProductId, CatalogRevision, OccurredAt
```

This is a proposed public catalog projection, not the private Product aggregate or
its domain-event history. Publish when these disclosed values change; a private
supplier-note edit need not emit anything. Schema version describes the contract;
CatalogRevision orders this product's public state; EventId identifies delivery;
OccurredAt is descriptive time. These roles are separate.

For the merge protocol below, Changed must contain the **complete replacement state
for this public projection**, including explicit nulls. A generic aggregate version
does not make unrelated partial patches safely discardable. Receiving a price patch
at revision 12 before a name patch at 11 and discarding 11 would lose the name update.
Business events such as PaymentCaptured may trigger effects and need their own
ordering and idempotency rules; they must not be collapsed into latest catalog state.

## Alternative 1: lazy reference loading

Load the producer's public representation only when a consumer actually needs a
missing product, then refresh or invalidate the local entry using selected events.
This avoids an upfront full scan when the consumer touches only a small subset.
Microsoft's cache-aside guidance documents loading on misses and warns that the
pattern does not guarantee consistency. Local caches can also diverge.
[Azure cache-aside pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside).

Recommendation: bound fetch concurrency, combine concurrent misses for one key and
define stale/unavailable behavior. A fetched older revision must not overwrite a
newer event applied while the request was in flight. Keep versioned deletion markers
or equivalent invalidation state so a late response cannot resurrect removed data.
This shifts producer load to first use; it does not eliminate load if every product
is eventually queried. Cold misses retain a runtime availability dependency. It is
unsuitable as the sole source for complete offline search or reporting.

## Alternative 2: public export plus overlapping integration events

Recommendation: use this when a consumer needs the whole public dataset but should
not enumerate live business endpoints. Export public records into immutable files
with a manifest, tenant/schema identity, versions and bounded import chunks. Consumers
download the same artifact from object storage. Producing it still costs resources;
reuse amortizes that cost across consumers.

The following is a **proposed protocol**, inferred from snapshot and durable delivery
primitives, not a PostgreSQL/broker feature that automatically joins the two systems:

1. Establish durable event capture **before the source snapshot begins**: bind a
   dedicated durable subscriber queue, or persist retained-log offsets O for every
   relevant partition. Confirm the binding/capture is effective. Keep the overlap
   until import and recovery finish.
2. Generate a fresh complete public export from a consistent producer database
   snapshot. PostgreSQL Repeatable Read provides stable visibility across successive
   queries in one transaction; separate transactions for each HTTP page do not.
   [PostgreSQL isolation documentation](https://www.postgresql.org/docs/current/transaction-iso.html).
3. Import records into an empty receiver projection, checkpointing completed chunks.
   Version and payload must have been read together. Avoid exposing partially built
   data where callers require complete population.
4. Apply the captured events. For each key, atomically replace local state only if
   the incoming public revision is newer. Equal revisions must agree; duplicates
   have no second effect. Removal records advance the stored revision too. Import
   can use the same conditional operation if events run concurrently.
5. Commit state changes with the local receipt/checkpoint before acknowledging
   delivery. Restart repeats unacknowledged work safely. Continue normal consumption
   after bootstrap; define readiness separately from merely finishing file import.

Example: the export contains product P at revision 7. An overlapping revision 8
event may arrive before or after that record is imported; revision checks leave 8
in either order. A committed revision 6 outbox message published late is also harmless.
A revision 9 deletion prevents delayed revision 7 import from recreating P.

The outbox is essential delivery plumbing: persist a selected outgoing message in
the same transaction as its source change, then relay it after commit. AWS documents
this atomic staging and possible duplicate delivery. An outbox solves delivery of
newly selected events, not initial population or unlimited historical retention.
[AWS transactional outbox pattern](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/transactional-outbox.html).

Unsent committed outbox messages are why broker offsets do not directly identify a
database snapshot boundary. Capturing before the export tolerates overlap and late
publication under the full-state contract above. It guarantees eventual convergence
only if every relevant transition is eventually delivered and capture has no holes.
An empty receiver queue can coexist with pending source outbox work. Strong claims
such as “current through source commit T” require additional coordinated publication
barriers. Per-key merging also does not recreate cross-aggregate atomic visibility.

**Reuse warning:** last week's export plus a subscription created today misses the
intervening changes. A reusable export needs its original overlap offsets in its
manifest and retained events from there, or a maintained bootstrap store. If that
history expires, reject the artifact for catch-up and generate a fresh export after
capture starts. Size and age retention limits both matter.

PostgreSQL documents concurrent consistent exports with pg_dump; this demonstrates
the snapshot primitive, not a recommendation to distribute private database dumps.
[PostgreSQL pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html).
LinkedIn's historical Databus architecture maintains a separate moving bootstrap
snapshot and serves new consumers there before catch-up, avoiding per-consumer
lookbacks against primary OLTP. It is CDC deployment evidence, not a recommendation
to adopt Databus or expose private tables.
[LinkedIn Engineering, 2013](https://engineering.linkedin.com/data-replication/open-sourcing-databus-linkedins-low-latency-change-data-capture-system).

For very large exports, Debezium's incremental snapshots demonstrate chunking while
streaming continues, using snapshot windows and reconciliation for overlapping keys.
That is a useful mechanism reference; copying it requires its watermark guarantees,
and does not turn raw database changes into a suitable public contract.
[Debezium PostgreSQL incremental snapshots](https://debezium.io/documentation/reference/stable/connectors/postgresql.html#postgresql-incremental-snapshots).

## Alternative 3: let broker retention carry bootstrap

RabbitMQ Streams retain an append-only log with non-destructive reads and selectable
starting offsets; ordinary queue semantics differ. Stream retention can discard old
segments by age or size. A new subscriber can replay retained public events without
asking the producer for them. Retention alone cannot reconstruct unchanged entities
whose only event has already expired.
[RabbitMQ Streams](https://www.rabbitmq.com/docs/streams).

A compacted Kafka topic can instead retain the final record for each product key.
Full public state records make that useful for constructing a local catalog; partial
deltas do not. Compaction removes superseded records while preserving offsets and
order, so gaps are normal. It retains the final **log record**, not the largest
CatalogRevision: publishers must preserve per-key order, or a version-aware
materializer must publish the current public state. Kafka also provides client quotas
for controlling shared broker load.
[Kafka log compaction and quotas](https://kafka.apache.org/43/design/design/).

Deletion retention bounds how long a rebuilding reader can take without missing
tombstones. Combining compact and delete cleanup also allows time/size removal of
old segments, including long-unchanged state. Configuration is part of the bootstrap
guarantee, not an incidental tuning detail.
[Kafka topic cleanup and deletion settings](https://kafka.apache.org/43/configuration/topic-configs/).

Debezium's outbox router illustrates CDC of **only the selected outbox records**:
application-owned payloads, unique event IDs and aggregate keys for partition order.
This can publish semantic integration events without publishing every business-table
change. CDC is an optional relay implementation.
[Debezium outbox event router](https://debezium.io/documentation/reference/stable/transformations/outbox-event-router.html).

## Implication for B1 and next review

The proposed overlap/version merge approach needs per-entity source concurrency and
atomic receiver writes, but does not inherently need a total Organization cursor or
serialization of unrelated producer writes. Broker progress tracks delivered records;
public revisions track entity state. A polling feed built directly on allocated SQL
sequence values has a different obligation: allocation is not commit order. PostgreSQL
sequences are immediately visible and not rolled back, explaining B1's gap and late
commit concerns.
[PostgreSQL sequence behavior](https://www.postgresql.org/docs/current/functions-sequence.html).

Recommendation: first choose a real receiver and whether it needs sparse references,
complete public current state, or historical business effects. For sparse use choose
lazy loading; for complete state compare export plus overlap with an existing retained
broker state topic. Keep ordinary selected events as the live communication path.
Assess B1 as an optional replay interface against that chosen requirement before
expanding it. A separate queue/worker controls backlog and concurrency but shares
database and broker capacity unless resources are explicitly isolated.

Only this research report is new work. No runtime tests were run and no new reusable
mechanism was proven. Existing B1 proof results remain evidence for its implemented
pull protocol, not proofs of these proposals. Public schemas, disclosure, revision
semantics, bootstrap readiness and retention stay consumer-owned policy. Any library
extraction, template configuration or sample implementation needs a separately
reviewed consumer and failure proofs for update/delete overlap, delayed publication,
duplicates, restart and expired export/history.
