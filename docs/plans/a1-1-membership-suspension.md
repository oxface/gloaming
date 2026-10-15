# A1.1: consumer-owned membership suspension/reactivation

Implementation authorized by the owner after the
[A1 assessment](../reports/access-authorization-assessment.md), 2026-10-16.
Topic: `feat/membership-suspension`, from approved assessment `fd8a10e` above fetched
main `7ea6652`. All implementation changes stay unstaged; commit approval is separate.
This scope adds consumer Contracts and native Access persistence/HTTP adoption, not a
Gloaming interface, OpenFGA service, role editor or RLS integration.

## Concrete consumer surface

`IMembershipAdministration` exposes an authorized point read and separate `SuspendAsync`
and `ReactivateAsync` intents. Each mutation identifies the target membership and expected
revision. The admitted Organization and acting human come from established operation
contexts, never request-body actor/Organization overrides. Contracts expose ordinary
membership/status/revision DTOs and explicit results, without EF, HTTP or context types.

Access owns a narrow persisted membership-administration grant. It supplies no business
data permissions. Every point read/mutation, including repeated requests, checks current
active membership and this grant inside the selected Organization. Unknown/foreign targets
and unauthorized callers receive the same non-disclosing result. Global identity lookup
remains independent of tenant context.

Membership revision advances once for an accepted transition. An equal-state request with
the current revision returns AlreadyInRequestedState without a transition audit; stale
revisions return Conflict, including Active→Suspended→Active. Removed→Active is excluded.
No generic idempotency/receipt system is added: after an ambiguous commit the caller can
read current authorized state, but cannot assert which attempt caused it.

## Native transaction and audit

The command owns a short native Access transaction. Serialize supported administration
within one Organization using its persisted Organization row before reading authority and
the target. Membership original-revision concurrency still protects the native update.
This allows a selected last-administrator rule to be checked atomically rather than using
two independent target-row revisions. Future grant/provisioning writers must join this
protocol before claiming the same invariant; direct SQL is outside the business Contract.

Compose existing Gloaming audit mapping/staging and explicit sync/async validation in
Access. The accepted membership transition and its audit commit together. Audit records
identify Organization, target membership/stable user, administrator, prior/new status and
revision, time and a versioned safe detail payload. No-op/denial attempt durability is
outside the accepted-change audit. Failed saves/known rollbacks leave neither participant;
lost commit responses remain ambiguous. No external side effect occurs before commit.

## Owner-selected product policies

- Block self-suspension and protect the last active membership administrator. Current
  authority and the target are read after acquiring the Organization lock, preventing two
  administrators from suspending each other based on stale authority.
- Preserve grants during suspension; they become eligible after reactivation only under
  current permission checks. Revoked grants are not recreated.

Already-admitted actor/tenant contexts remain immutable under every choice. Subsequent
fresh protected human admission uses the existing persisted Active lookup. Global identity,
other active Organizations, public-route exceptions and historical attribution are retained.

## Adoption and proof scope

Add native authenticated, antiforgery-protected suspend/reactivate endpoints plus an
authorized point read to HttpIdentityDemo. Keep existing middleware order and module
Contracts. Extend the finite setup only to demonstrate administration explicitly; migrating
an existing database does not silently grant administrator privileges.

Prove authorization/foreign targets, input/context requirements, repeated/current/stale/ABA
outcomes, concurrent identical/opposing commands, chosen admin safeguards and grant policy.
Prove audit/commit rollback and preserved historical rows; same-cookie protected business
read/mutation denial, global identity/second-Organization access, Suspended in-flight context
retention, reactivation under current authority, and fresh non-HTTP admission. Demonstrate
distinct company-owned system work and continuing-human work policies without a worker or
offboarding framework. Preserve populated pre-A1.1 schema migration compatibility.

Review the consumer Contract, Access command/permission/audit code and migration, HTTP
bridge and independent PostgreSQL/HTTP proofs. Report fresh results separately from A1's
source inspection and historical E3/E9 evidence. No new reusable mechanism is presumed.
