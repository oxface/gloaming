# ADR 0016: composable event capture and reactions before native save

Status: accepted and implemented. Owner-selected EventReactions consolidation, 2026-10-15,
supersedes the earlier Gloaming.Domain positioning and aggregate-bound collection.

Provide platform-only Gloaming.EventReactions and optional Gloaming.EventReactions.EntityFrameworkCore.
The useful capability is ordered local capture/dispatch, not a Domain or DDD foundation. Consumers
compose PendingEvents into any object, use an operation sink or forward facts from a pure model.
Remove the supplied AggregateRoot and mandatory event marker. Event types remain ordinary consumer
records, including value records.

EF registration selects tracked buffers through CollectFrom<TEntity>(selector) and other scoped
buffers through CollectSources(provider, context). There is no base-type or property discovery.
Missing source registration is a consumer integration omission, not something a tracker mirror fixes.
Model requirements remain stable metadata; scoped providers/handlers stay outside cached models.

Retain exact-type serial reactions, bounded cascades, capture order across participating sources and
clear-before-handling. Use shared Gloaming.EntityFrameworkCore preparation/change detection/validation
before one native save. Both sync and async paths exist; sync rejects async-only reactions. The caller
owns the active transaction and commits after saving returns. Any failure requires a fresh operation.

Use bounded reactions for required atomic intra-module work and explicit application orchestration for
complex dependencies. Registration is routing rather than a business dependency graph. Do not add
a decider abstraction, business-history store or another event-sourcing model to justify this package.
Those need separate consumer evidence. Durable saga state remains module-owned; the
[local module delivery consumer](../../samples/Wholesale/Worker/Local/README.md) separately
exercises retained intake and recovery across module commits.

No Stage, acknowledgment, owned transaction, aggregate save guard, universal tracker protocol or
late-event completion enforcement is added. Native false acceptance and callback/bulk-write bypasses
remain visible limits. See the [execution contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md)
and [consolidated evidence](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/event-reactions-consolidation.md).
