# ADR 0011: Explicit audit participation in the owning native transaction

Status: accepted and implemented, 2026-10-09. See [the family contract](../../src/Gloaming.Auditing/docs/transactional-audit.md) for current setup.

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
and clock. Correlation, causation and trace IDs remain separate diagnostic metadata rather than
required business-audit fields. Audit wrappers need neither Activity nor incoming message context.
Nullable SubjectKey permits entries without an individual item/aggregate; SubjectType remains
meaningful. ConfigureAudit includes a native item-timeline index, with customization left to the
consumer. Payload records and schema identities live in consumer wrappers.
TenantKey is optional. AuditRecord is a reference-identity EF class; the pending guard compares
exact JSON text rather than structural value equality.

Sales profile mutations and Inventory inbox stock issues exercise the capability, alongside an
ordinary tenantless independent EF consumer. Selected shared save composition validates staged
audit without automatically choosing business changes. No auditing middleware, shared transaction
runtime or template preset is supplied. Denial audit,
initiator propagation, tamper evidence, retention/query services and other providers remain
separate review/proof gates.

See the [library-local contract](../../src/Gloaming.Auditing/docs/transactional-audit.md) for supported composition and limits.
