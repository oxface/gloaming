# 0003: Resolve Access identities and admit Organizations before tenant publication

Status: accepted and implemented, 2026-10-05. This is application policy.

## Context

The actor and tenancy libraries are independently adoptable and grant no authorization.
An application user can belong to multiple Organizations. Access must resolve global
issuer/subject links and selected-Organization membership before tenant context exists.
Combining admission with a current-tenant filter would require an artificial scope or a
filter bypass, while caching memberships in a cookie would delay revocation.

## Decision

Keep Access as consumer-owned code with its own schema, DbContext and native migrations.
Its registry uses explicit user and Organization keys, with no current-tenant filter.
External identity pairs resolve exactly to stable global application users; ordinary mapping
does not create users, link by email or update profiles. The HTTP consumer maps those domain
identities into the existing technical actor/tenant keys explicitly.

Protected Organization admission reads active membership afresh before publishing tenancy.
At most one current Active/Suspended membership exists per user/Organization; removed
membership history may coexist. Persist statuses explicitly as 1/2/3 and reject undefined
values. Selected Organization identity and active membership are read in one statement.

Public catalog access is an explicit consumer endpoint exception, available to anonymous
callers and mapped authenticated non-members. Native AllowAnonymous only selects native
authentication policy. Unknown or inaccessible Organization responses remain indistinguishable.
The actor mapping rule still rejects unmapped authenticated principals on public endpoints.

Committed revocation blocks a subsequent admission query. Already-admitted operations retain
their immutable context. Admission concurrent with revocation may observe the earlier state;
this does not guarantee authorization remains current through a later business transaction.

## Consequences and limits

Neither technical core nor HTTP adapter acquires Access, EF or a dependency on its peer.
Access reads use native request-scoped persistence and propagate cancellation/database faults.
The host explicitly runs setup; startup never migrates, seeds, saves or retries requests.
This schema is a consumer registry, not unrestricted access to tenant business data.

Membership management, invitations, roles and resource permissions remain application policy.
This decision covers admission and context publication. No reusable Access library is
justified by this consumer alone.

See [consumer setup](../../samples/Wholesale/Api/README.md) and [Access ownership](../../samples/Wholesale/modules/Access/README.md).
