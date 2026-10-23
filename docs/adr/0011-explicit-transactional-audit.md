# ADR 0011: Explicit audit participation in the owning native transaction

Status: accepted and implemented. Original owner-approved scope, 2026-10-09; native provider
configuration replaced the thin PostgreSQL mapping package at `d63f567`. Current setup is in
[the family contract](../../src/Gloaming.Auditing/docs/transactional-audit.md).

Use an independently adoptable `Gloaming.Auditing.EntityFrameworkCore` package. Consumers
explicitly construct AuditEntry and call typed IAudit<TDbContext>.Stage within the native
transaction that owns the selected business effect. The library captures owned JSON and
immutable ActorIdentity attribution, maps one provided row and validates exact context/
transaction association in explicitly installed native save guards.

Native EF owns atomicity; the library does not create transactions, save, commit, retry or
publish. Consumer code decides which accepted change requires audit and supplies trusted
actor/optional initiator, exact opaque identifiers, observation time and classified details.
There is no actor-context, clock, tenancy or authorization fallback. Business outcome/reason
vocabulary, sensitive disclosure, query visibility and retention remain consumer-owned.

Reuse existing ActorIdentity values to preserve Human/System/Anonymous distinction and
optional initiator without an audit-specific identity model. The package depends on that core
plus shared Gloaming.EntityFrameworkCore save composition and EF Relational. Native DI wiring
belongs to consumers. PostgreSQL consumers select JSONB with native EF mapping; no separate
provider convenience package is required.

Guarded inserts retain the staged envelope and active native transaction; tracked edits/deletes
are rejected. The guard cannot detect omitted Stage calls or protect raw/bulk SQL. Selected
consumer workflows prove required participation with independent test-only deferred commit
constraints. Privileged retention and database grants remain separate consumer concerns.

Consumer-owned scoped wrappers gather established attribution/tenant, generated entry ID
and clock. The owner removed correlation, causation and trace IDs under YAGNI: there is no
current business use that earns these audit fields. Telemetry remains host-owned; existing
messaging metadata remains separate. The audit wrappers need neither Activity nor incoming
message context. Nullable SubjectKey permits entries
with no individual item/aggregate; SubjectType remains meaningful. Native item timeline
index is included in ConfigureAudit by default, with native customization remaining a
consumer choice. Payload V1 records and schema
constants live together in consumer wrappers; no automatic schema inference is introduced.
TenantKey is the final optional constructor parameter. AuditRecord remains a reference-identity EF class;
exact pending JSON text is deliberately compared explicitly. See
[the primary-source findings](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e9-audit-context-research.md).

Sales profile mutations and Inventory inbox stock issues exercise the capability, alongside an
ordinary tenantless independent EF consumer. Selected shared save composition validates staged
audit without automatically choosing business changes. No auditing middleware, shared transaction
runtime or template preset is supplied. Denial audit,
initiator propagation, tamper evidence, retention/query services and other providers remain
separate review/proof gates.

[Library-local contract](../../src/Gloaming.Auditing/docs/transactional-audit.md) and
[execution report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e9-explicit-transactional-audit.md) record the practical limits.
