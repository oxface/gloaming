# Sparse consumers, integration events and initial state

Research date: 2026-10-14. Question: how can a new Order service acquire only
referenced Product information, remain responsive during producer outages, and avoid
snapshot/event races? Primary examples were investigated before mapping them to the
suggested approaches. This is research and proposed application design, not an ADR,
implementation authorization or proof of a new library capability.
Follow-up: B1 was closed as an [assessment](b1-retention-and-contract-assessment.md);
experiment code and patch artifacts were removed at the owner's request.
Its implementation descriptions below are dated context, not current sample capabilities.

## First establish which information Order actually needs

There are three different requirements: purchase facts accepted for an order,
latest public information about selected products, and a complete searchable catalogue.
They do not require the same replication protocol.

**Documented commerce example:** commercetools creates a cart line item from a public
ProductProjection snapshot when a variant is added. An Order records a transaction,
copying line items and prices from a Cart or agreed Quote. Later financial amendments
use explicit Order Edits. This is a vendor's domain/API example, not evidence of its
internal microservice deployment or availability guarantees.
[commercetools Carts and Orders](https://docs.commercetools.com/api/carts-orders-overview).

Shopify makes the distinction observable: an order's `line_item.title` returns the
title at purchase time; `line_item.product.title` returns the current product title.
[Shopify line item title history](https://shopify.dev/docs/api/liquid/objects/line_item).

**Reference application:** Microsoft's eShop Order aggregate stores product ID, name,
unit price, discount and picture with its own line items. Its documented Basket
subscriber handles a ProductPriceChanged event by updating matching items already in
baskets. Unknown products do not require creating catalogue entries. This demonstrates
sparse interest, but the simplified reference handler does not prove race-free
initial hydration, ordering or production-scale scanning.
[Order source](https://github.com/dotnet/eShop/blob/main/src/Ordering.Domain/AggregatesModel/OrderAggregate/Order.cs),
[Basket subscription example](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/subscribe-events).

**Inference for our example:** capture trusted purchase facts from checkout/quotation
in the Order command or message. A later catalogue rename or price change should not
silently rewrite a purchase agreement. Price acceptance, stock reservation and product
eligibility remain business decisions; a recent cached description does not authorize
a sale. Browser-supplied prices need producer validation or a trusted quote mechanism.
Only introduce a changing local Product view for an identified requirement, such as
current handling instructions or an active cart. This can remove much of the apparent
bootstrap problem before designing a feed.

## Existing solutions for acquiring changing state

### 1. Fetch missing references: Stripe's public integration

Stripe explicitly does not guarantee webhook order. Its documented example receives
`invoice.paid` before creation events and retrieves the missing invoice, charge and
subscription using IDs carried by that event. Stripe offers snapshot and thin event
formats: handlers can use event-time data or fetch current resource data. The stated
delivery-order limitation must be handled by the integration.
[Stripe webhook guidance](https://docs.stripe.com/webhooks).

This is a concrete counterexample to the idea that event-driven integration forbids
HTTP lookups. It accepts a dependency when information is missing. It does not establish
a universal entity revision/snapshot protocol: API schema version is not entity state
version, and fetching current state does not reconstruct every historical business
effect. For Order, loading a missing public Product on first reference is comparable;
copying an authoritative accepted price into the order is a separate responsibility.

### 2. Sparse caches with race prevention and repair: Meta and Hazelcast

Meta documents a production race in which a database fill response containing old data
arrives after a newer invalidation and overwrites it. A version guard helps, but eviction
can lose the guard; its article also describes mismatched value/version reads and a
real error-handling bug. Their operational response includes consistency monitoring
and focused tracing around fills and invalidations.
[Meta, Cache made consistent](https://engineering.fb.com/2022/06/08/core-infra/cache-made-consistent/).

Hazelcast Near Cache populates local data through key reads and invalidates entries
asynchronously. Its documentation explicitly permits stale reads. It detects missed
invalidations and periodically reconciles, making affected entries unreachable so a
later read fetches again.
[Hazelcast Near Cache](https://docs.hazelcast.com/hazelcast/5.4/cluster-performance/near-cache).

**Inference:** lazy loading is established practice, but a sparse replica needs more
than a cache dictionary. Preserve revision/deletion knowledge across in-flight loads,
define expiry and reconciliation, combine concurrent misses, and bound fetch concurrency.
An evicted payload may be harmless; evicting the only evidence that a newer revision
was seen can allow stale resurrection. These are examples of mechanisms and operational
obligations, not reasons to copy an entire catalogue.

### 3. Combine initial state and updates: NATS KV and Axon

NATS JetStream KV demonstrates a watcher for a key or pattern that first yields
the current matching values, signals completion of the initial values, then yields
updates including deletions. Bucket history defaults to one value per key rather than
an unlimited event history. Revisions use the underlying stream sequence and need not
be consecutive for a particular key.
[NATS runnable example](https://examples.nats.io/examples/kv/intro/go/),
[NATS Go watcher options](https://github.com/nats-io/nats.go/blob/main/jetstream/README.md),
[NATS KV concepts](https://github.com/nats-io/nats.docs/blob/master/nats-concepts/jetstream/key-value-store/README.md).

This closely matches requesting selected products and keeping them updated without
replaying every historical event. It assumes a maintained public state store in the
broker. Getting data separately and then creating a watcher is not the same protocol;
that can introduce a gap. Producer-to-state-store publication, access controls, expiry,
reconnection and retained deletion state still require design.

Axon's gift-card reference application uses subscription queries for filtered current
results and subsequent updates. Its explanation explicitly identifies the registration
race, collects updates while the initial query runs, and recommends whole replacement
records because the initial result and updates can overlap. This is a query-result
subscription, not replay of private aggregate history.
[Axon explanation and demo](https://www.axoniq.io/blog/introducing-subscription-queries),
[Demo repository](https://github.com/fransvanbuul/gc-subscriptions).

**Inference:** snapshot plus live updates can be a first-class producer API. Session
buffers and live subscriptions alone do not provide durable business processing through
arbitrary outages; reconnect must re-establish state or resume retained progress. A
bounded buffer overflow must fail visibly, leading to resynchronization.

Kafka Streams foreign-key joins offer another implemented per-reference protocol. A
left-side record sends a subscription to the right-side task, receives current matching
data and then receives updates. The implementation carries a hash of the requesting
record to reject a delayed response after its reference has changed. This demonstrates
that a response must still match current interest, not merely have a valid correlation
ID. The right-side table still exists across the cluster; this is a protocol reference,
not proof of eliminating catalogue storage everywhere.
[Implementation authors' walkthrough](https://www.confluent.io/blog/data-enrichment-with-kafka-streams-foreign-key-joins/),
[Apache response processor](https://github.com/apache/kafka/blob/trunk/streams/src/main/java/org/apache/kafka/streams/kstream/internals/foreignkeyjoin/ResponseJoinProcessorSupplier.java).

### 4. Request missing state asynchronously: enrichment and durable workflows

The Enterprise Integration Patterns authors provide a runnable Content Enricher:
an SQS message carries entity IDs, EventBridge Pipes invokes Lambda to batch-fetch
DynamoDB records, and enriched messages reach another queue. This demonstrates targeted
enrichment rather than catalogue replication. The example fetches current state; it
does not promise event-time values or future change capture.
[Content Enricher example](https://www.enterpriseintegrationpatterns.com/patterns/messaging/DataEnricher.html).

MassTransit's state-machine request example sends ValidateOrder, persists the saga
while waiting, and reloads it on OrderValidated, a fault or timeout. A stored request
ID correlates responses; the response is processed at the saga endpoint.
[MassTransit state-machine requests](https://masstransit.massient.com/guides/saga-state-machines/request).

NServiceBus likewise recommends collecting required external information via messages
and coordinating external calls through dedicated handlers, with persisted state and
out-of-order handling.
[NServiceBus sagas](https://docs.particular.net/nservicebus/sagas/).

**Mapping to the proposed snapshot channel:** use a request such as
`GetProductReference(ProductId, RequestId)` and a targeted response containing public
state and its revision. The response is a document answering a query; publishing a
synthetic ProductChanged event to every ordinary subscriber can misleadingly suggest
a business change occurred. A durable reply endpoint and correlation usually fit crash
recovery better than a temporary queue tied to one process. Transport alone does not
establish the state/update boundary.

The useful improvement over blocking HTTP is accepting work now and completing it
later. Product must eventually respond, but Order can retain pending work through its
outage. If the UI still demands an immediate completed order, replacing HTTP with a
queue does not remove that logical dependency.

## What the version rule solves, and what it cannot solve

Elasticsearch provides a concrete external-versioning operation: index a replacement
document only when its source version exceeds the stored version. Its documentation
expressly permits asynchronous source updates to arrive out of order. Local indexing
sequence numbers serve a different concurrency purpose.
[Elasticsearch external versioning](https://www.elastic.co/docs/api/doc/elasticsearch/operation/operation-index).

The rule works for **complete replacement state for one public view**. As an inferred
application protocol, an HTTP snapshot at revision 20 and a replacement event at 21
can arrive in either order; an atomic conditional write leaves 21. Duplicates do not
advance state twice. Equal versions with unequal payloads are a contract violation,
not a reason to silently pick one.

It is unsafe to generalize this to every event. A revision-21 price patch arriving
before a revision-20 name patch does not contain the name; dropping 20 loses a change.
A PaymentCaptured event can cause an independent business effect even when newer
account state exists. Snapshot version cannot replace effect-specific idempotency.

A deletion needs a revision fence too. Elasticsearch retains deleted-document versions
only for a limited interval controlled by `index.gc_deletes`.
[Elasticsearch deletion version retention](https://www.elastic.co/docs/api/doc/elasticsearch/operation/operation-delete).

Our inferred protocol must therefore retain tombstones/fences long enough to cover
delayed responses and events, or explicitly forbid accepting old work after reset.
Product ID reuse requires generation identity or equivalent protection.

Contract/schema version describes how to decode a message. Public-view revision orders
that entity's disclosed state. Event ID deduplicates deliveries. These identifiers
have different jobs; a timestamp is not automatically a substitute for any of them.

Version comparisons prevent stale overwrites; they do not detect a missing update.
For example, snapshot 20 plus missing event 21 can leave 20 indefinitely. Ensure change
capture is active before taking the snapshot, use an integrated watch protocol, or
have authoritative refresh/reconciliation. If using patches, predecessor information
or ordered public-view revisions must identify gaps. Private aggregate revisions can
skip legitimately because private changes need no integration event.

The source must read the public values and revision coherently. A stale read replica
can return revision 19 after the consumer has ignored event 20: no amount of local
locking can recover an event that was never retained. Requesting a minimum observed
revision, retrying against an authoritative source, or establishing capture before
fetching are possible policies requiring explicit proofs.

## A proposed sparse consumer flow

This is synthesis of the examples, not a documented ready-made Product/Order solution.
For complete public state messages and eventual convergence:

1. On first business reference to Product P, move from `Absent` to durable interest
   and `Hydrating` state. Enable capture for P before reading its snapshot. Deduplicate
   concurrent hydration requests by tenant/key and request generation.
2. Intake relevant events durably. For genuinely unreferenced products, state-only
   catalogue updates can be deliberately ignored. Hydrating every unknown key on every
   catalogue event would defeat sparsity. For Hydrating products retain events
   or merge complete replacements. Required business-effect events cannot be ignored.
3. Fetch or request public state. Do network I/O outside the local database transaction.
   Unrelated keys and snapshot responses must continue processing.
4. In a short local transaction, merge the coherent snapshot and captured updates by
   revision, retain deletion knowledge, and advance local receipts. With patches, apply
   only a valid chain after the snapshot, or refresh rather than guessing across a gap.
5. Mark the reference `Ready` under explicit freshness policy and resume waiting work.
   A response older than a required revision stays pending. Retry/timeouts, permanent
   absence, eviction and restart are explicit states.

Do not hold the whole inbox or a database lock while awaiting Product: that can block
the very snapshot response needed to proceed and stall unrelated work. Broker intake,
per-key pending work and short atomic receiver updates are separate concerns. Existing
inbox APIs must be assessed before deciding whether to leave a message unprocessed or
complete its intake handling after durably transferring it to pending work.

## Ideal and pragmatic choices

| Need | Pragmatic choice | Stronger independence | Cost or limitation |
| --- | --- | --- | --- |
| Completed order history | Trusted purchase/quote facts stored in Order | Facts carried in checkout messages | Does not establish current stock or eligibility |
| Occasional current references | HTTP miss plus bounded cache and selected updates | Durable async hydration with pending work | Cold references await producer; freshness policy remains |
| Selected keys with ongoing state | Versioned hydration plus change capture | Public state store with per-key initial-and-update watch | Maintained source view and reconnect protocol |
| Complete catalogue/search | Controlled export plus overlapping retained changes | Separate reusable bootstrap/state store | Full-population boundary, retention and deletions |

Ideal independent service completion requires every needed fact already local or
carried in the initiating message, or permission to wait asynchronously. When a new
product is unknown and its owner unreachable, the consumer cannot invent correct data.
Serving a cached description during an outage is different from approving a current
price or reserving stock. Independence is a business-operation property, not solely
a choice of HTTP versus messaging.

Bulk bootstrap remains justified for complete views, not merely because a service is
new. The earlier [export/bootstrap research](integration-event-bootstrap-research.md)
covers reusable artifacts and overlap. Sparsity changes both load and correctness
obligations: previously irrelevant keys become relevant at different times.

## Repository outcome and remaining evidence

The discarded [B1 experiment](b1-replayable-integration-feed.md) was a
selective retained Sales profile after-image pull feed. It proves that source's paging
and progress protocol; it has no broker publication or historical initial population.
Existing [Messaging capabilities](../../src/Gloaming.Messaging/docs/capabilities.md)
already supply separately composed outbox/inbox mechanics. Neither implies the new
sparse hydration protocol above is implemented.

Only this report was added. Existing staged work, the preceding research report, archive,
migrations and code were preserved. No runtime tests were run and **no new reusable
mechanism was proven**. Source evidence supports alternatives, not a library extraction.
Choose a consumer requirement before revising B1 or proposing B2. Any later sample
should prove in-flight older responses, active-interest registration, stale source reads,
delete/recreate, duplicate replies, restart, response timeout and patch gaps with real
database/broker behavior. Disclosure, readiness and freshness remain consumer policy;
there is no new library interface or template finding in this research slice.
