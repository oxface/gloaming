# ADR 0011: Explicit audit participation in the owning native transaction

Package refinement, 2026-10-22: the convenience adapter described below is retired.
Consumers use the native configuration shown in the
[retirement report](../reports/thin-adapter-retirement.md). The underlying transaction
and diagnostic guarantees are unchanged; the original decision is retained below.

Status: interface and bounded scope owner-approved, 2026-10-09; implementation remains
unstaged for line-by-line review. [Reviewed scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e9-explicit-transactional-audit.md).

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
and EF Relational only. Native DI wiring belongs to consumers; no unused DI dependency or
standalone core package is introduced. The owner-requested optional
`Gloaming.Auditing.EntityFrameworkCore.Postgres` package composes relational mapping with
JSONB details; it depends on the EF package and Npgsql, keeping provider setup separate.

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
[the primary-source findings](../reports/e9-audit-context-research.md).

Sales profile mutations and Inventory inbox stock issues exercise the capability, alongside an
ordinary tenantless independent EF consumer. No wire/Contracts change, auditing middleware,
SaveChanges interceptor, shared transaction runtime or template preset is added. Denial audit,
initiator propagation, tamper evidence, retention/query services and other providers remain
separate review/proof gates.

[Library-local contract](../../src/Gloaming.Auditing/docs/transactional-audit.md) and
[execution report](../reports/e9-explicit-transactional-audit.md) record the practical limits.
