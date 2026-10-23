# Native persistence and delivery integration

Distilled from the 2026-10-06–22 research. [Evidence dates and limits](README.md) apply.
Marten, Wolverine and MassTransit supplied behavioral comparisons; they are not dependencies
or a checklist of features Gloaming must reproduce.

## Separate declarations, composition and execution

A model declaration describes stable storage/capability metadata. Native DI supplies scoped
handlers and context-specific bindings; selected save hooks execute preparation/validation.
Model construction must not capture scoped services or start workers. Context/model identity
matters when the same provided row type is mapped into different module schemas.

The research favored native typed contexts and explicit operation ownership over a shared
ambient persistence runtime. A successful SaveChanges or accepted tracked state is not proof
that an outer transaction committed. Similarly, a publication return is not receiver business
completion. These boundaries drive different rollback, uncertain-result and recovery proofs.

Current [EF capability composition](../../src/Gloaming.EntityFrameworkCore/README.md)
and [Messaging contracts](../../src/Gloaming.Messaging/docs/capabilities.md) own the actual
supported ordering and failure guarantees. The optional hosting package is a separate choice
from persistence. Consumers retain native SDK/exporter setup, transport topology, supervision
and restart policy; no framework-wide bus/telemetry configuration follows from adoption.

## Keep different stored records distinct

Per-stream facts, required inline aggregate state, business audit, outgoing intent, incoming
receipts and continuation progress solve different problems. A history validator does not
supply a database snapshot; stream append does not imply delivery; observability cannot be
used as a permanent deduplication registry. Upcasting shape and changing projection meaning
also have different rollout/rebuild consequences.

The selected EventSourcing design keeps one exact-head required aggregate state, native
optimistic stamps and separate full-replay maintenance. Earlier prepared append handles and
pre-read locking are superseded. Native EF queries remain the first option before secondary,
async or multi-stream projection machinery. Current guarantees and deferred proof obligations
remain in the [family contract](../../src/Gloaming.EventSourcing/docs/capabilities.md).

## Original evidence

[Marten comparison](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/marten-event-sourcing-reference.md),
[remaining-capability reference review](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/remaining-capability-reference-review.md),
[EF interception research](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/ef1-capability-interception-research.md),
[external integration patterns](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/ef1-external-patterns.md) and
[MassTransit comparison](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/ef1-masstransit-applicable-patterns.md) retain version/source pins,
alternatives and their original unexecuted research status. Primary references included
[MassTransit's pinned registration/model source](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/EntityFrameworkOutboxConfigurationExtensions.cs),
[.NET distributed tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
and [W3C Trace Context](https://www.w3.org/TR/trace-context/).
New abstractions require a concrete consumer and complete adoption/failure evidence under the
[extraction gate](../plans/library-extraction.md), rather than another wrapper around native APIs.
