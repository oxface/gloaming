# Domain and module composition

Distilled from the 2026-10-09–15 research. [Evidence dates and limits](README.md) apply.

## Prefer a concrete decision model

The useful starting point is ordinary consumer code: domain values, immutable candidates or
entities, explicit command methods and native query projections. A complex domain may earn
aggregate behavior; simple CRUD need not adopt the same model. A CQRS split does not itself
justify a mediator, generic repository, common aggregate base or separate database.

Local facts can express consequences of an accepted decision. Consumers may compose buffers,
return plain facts from a pure model or orchestrate required changes directly. Captured source
order and serial routing are technical contracts; they do not define a business dependency
graph. Reactions that repeatedly query changing state need an explicit account of which
state they observe. Complex dependent calculations are often clearer in one application method.

The owner selected [EventReactions](../../src/Gloaming.EventReactions/docs/local-dispatch.md)
for bounded ordered intra-module reactions while retaining native save/commit ownership.
Its contract, including omitted sources and late/bulk callback limits, replaces earlier
aggregate-bound and Gloaming.Domain experiments. It does not supply a DDD runtime.

## Distinguish module-call guarantees

| Composition | Obligation |
| --- | --- |
| Direct synchronous module Contracts | Clear admission and failure propagation. Independently committed effects do not gain crash recovery merely because calls share a process. |
| One shared native database transaction | Explicit connection/transaction participation, compatible ownership and separate provider/consumer proof. This remains unsupported as a general library guarantee. |
| Durable local or broker delivery | Retained intent/intake, receipt and local effects committed together, retry/deduplication and process-death recovery. |
| Compensating process | Module-owned progress, correlation and semantic idempotency; compensation is a business action with its own possible failure. |

Wholesale's [local delivery](../../samples/Wholesale/Worker/Local/README.md) and
[broker workflows](../../samples/Wholesale/Worker/Broker/README.md) exercise selected durable
paths using current messaging. The optional [continuation runner](../../src/Gloaming.Messaging/docs/workflows.md)
removes repeated persistence/request bookkeeping. It supplies neither human assignments nor
a generic scheduler, parallel graph interpreter or automatic compensation policy.

## Original evidence

[Independent domain-event reassessment](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/domain-events-independent-reassessment.md)
retains ordering/state-observation counterexamples and comparisons;
[module consistency research](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/d2-3-module-consistency-and-coordination.md) compares
shared transactions, synchronous calls and durable coordination;
[domain shapes/EF mapping](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/ddd-domain-shapes-and-ef-mapping.md) explains immutable
candidates versus persisted entities. These are dated evidence, not new queued extractions.

Primary references included
[Microsoft CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs) and
[domain-event guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation).
Use the current [application patterns](../application-patterns.md) and
[ADR 0016](../adr/0016-local-domain-event-dispatch.md) for the selected repository boundaries.
