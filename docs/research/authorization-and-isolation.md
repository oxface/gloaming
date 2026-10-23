# Authorization and tenant isolation

Distilled from the 2026-10-14–17 research. [Evidence dates and limits](README.md) apply.

## Keep authority explicit

Authentication identifies a caller; actor attribution and tenant selection do not authorize
business access. Access owns the sample's user/Organization membership lifecycle. Business
modules own resource permission checks and predicates, including internal and worker calls.
A native HTTP policy alone does not enforce those non-HTTP entry points.

For lists, counts, exports and AI source selection, checking only a returned page cannot
establish complete authorized results. Apply the owning permission predicate before matching
count and deterministic pagination, or specify a candidate-scan/exhaustion contract. An
index recheck can reject stale allows; it cannot discover newly allowed rows absent from an
old index. These are consumer correctness obligations, not a reason for Tenancy to depend on
Access or an authorization engine.

The historical OpenFGA comparison found useful graph-policy features, but also separate
model/version, tuple authority, cross-store atomicity and query-completeness obligations.
Its engine/API/SDK pins and source-level ListObjects limitations are in the
[original primary-source matrix](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/access-authorization-primary-sources.md).
Do not infer current engine behavior or production suitability from that dated matrix.
No OpenFGA package or permission framework is selected.

## Isolation is another layer

The research distinguished EF filtering/tracked-write checks from database RLS. RLS adoption
needs deployed policies, actual restricted-role/grant evidence, transaction-local setup before
protected SQL, and separate migration/maintenance authority. A configured interceptor or
FORCE policy flag alone does not establish those conditions. Connection pooling and new
transactions require explicit lifecycle proofs; tenant equality still supplies no resource
permission within the tenant.

An application-set PostgreSQL setting remains trusted application input. Its threat model
does not contain arbitrary SQL using compromised runtime credentials. Keep that limitation
visible when evaluating optional RLS rather than treating it as an unforgeable identity.

## Current owners and original evidence

The later implementations supersede research-era statements that RLS was unimplemented:
[Persistence's contract](../../src/Gloaming.Persistence/docs/capabilities.md),
[independent transaction-local utility](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md),
[Sales permissions](../../samples/Wholesale/modules/Sales/README.md) and
[restricted HTTP composition](../../samples/Wholesale/Api/README.md#optional-sales-http-row-security).
Their current setup/limits are authoritative.

Original [A1 assessment](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/access-authorization-assessment.md),
[A2 assessment](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/tenant-authorization-assessment.md) and
[RLS primary-source matrix](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/tenant-authorization-primary-sources.md) retain rejected
alternatives, version pins and unexecuted proposals. Primary references included
[ASP.NET Core resource authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0),
[PostgreSQL 18 row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html) and
[OpenFGA source-of-truth guidance](https://openfga.dev/docs/best-practices/source-of-truth).
No new permission model, deferral or runtime guarantee is selected by this brief.
