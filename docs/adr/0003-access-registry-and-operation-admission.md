# 0003: Resolve Access identities and admit Organizations before tenant publication

Status: accepted design direction in the owner-approved E3.3 plan, 2026-10-05.
Implementation was owner-reviewed and checkpointed as `20a02be`; this is consumer
sample policy.

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

Membership management, invitations, roles, session invalidation and stronger mutation-time
authorization need separate review. Assembly-level module isolation and persisted Inventory
ingress remain E3.4 work. No reusable Access library is justified by this consumer alone.

See [the approved plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-3-persisted-access.md),
[consumer setup](../../samples/Wholesale/Api/README.md) and
[execution report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e3-3-persisted-access.md).
