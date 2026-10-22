# Consumer-owned Access

Access owns the sample's global users, exact issuer/subject links, Organizations and
memberships. It remains consumer source, with separate Contracts and persistence. Its
identity/admission queries require no selected tenant; the independently adoptable actor
and tenancy libraries have no dependency on Access.

## Membership administration

`IMembershipAdministration` exposes an authorized point read, SuspendAsync and
ReactivateAsync. The caller establishes trusted human actor and selected Organization
context; the implementation rechecks the caller's current Active membership and
`CanAdministerMemberships` grant on every call. The narrow persisted grant applies only
to this Access capability. It does not define any Sales, Inventory or private Personnel
permission, and this slice adds no general role editor or grant-management API.

The owner selected these sample policies:

- Block self-suspension and preserve an active membership administrator.
- Retain grants during suspension. Reactivation makes retained grants eligible under
  current checks; it never recreates a grant revoked in the meantime.
- Reactivate only Suspended memberships. Removed/reinvitation is a separate lifecycle.
- Audit accepted changes atomically; no-op/denial attempt logging is separate policy.

Use native provider registration plus the existing context registrations/admission:

```csharp
services.AddDbContext<AccessDbContext>(options =>
    AccessDatabase.Configure(options, connection));
services.AddAccessQueries();
services.AddMembershipAdministration();

// Inside one freshly admitted human operation scope:
var observed = await administration.ReadAsync(membershipId, cancellationToken);
var outcome = await administration.SuspendAsync(
    new MembershipChange(membershipId, observed!.Revision), cancellationToken);
```

`ReadAsync` is for a known target ID; member discovery/listing is not supplied. Handle a null
read and every result in actual consumer code. No body-supplied actor, Organization,
arbitrary status value or grant update is accepted. Global identity resolution still uses
the exact validated external identity, never an email-based link.

| Result | Meaning |
| --- | --- |
| Changed | This command committed the status transition, one revision increment and its audit. |
| AlreadyInRequestedState | Current revision matches and status already matches; no mutation/revision/audit. |
| Conflict | Expected revision is stale, including a change away and back, or native revision/Organization concurrency lost. Reload in a fresh scope; no automatic overwrite/retry. |
| NotFound | Target missing/foreign, Organization absent or caller lacks current admission/administration authority. These are deliberately not distinguished. |
| InvalidTransition | A visible Removed membership cannot be suspended/reactivated. |
| SelfSuspensionNotAllowed / LastAdministrator | Selected administrative safeguard refused the transition. With only one active administrator, LastAdministrator takes precedence over self-suspension. |

An identical retry carrying its old revision conflicts after a successful change. To observe
the current state, read again; an intentional equal-state request using the new revision is
a no-op. There is no operation receipt or exactly-once command claim. A commit response
lost after database success can mean both status and audit already committed; reconcile
through a fresh authorized read. That read cannot identify which attempt caused a change.

## Native scope, transactions and audit

The command requires a selected Organization, a human actor and a clean owning context;
use a fresh scope per administration operation. The command owns its short explicit native
ReadCommitted transaction, save and commit. It does not nest in a caller transaction.
All supported administration mutations lock the persisted Organization row before reading
current administrative authority and target state. This prevents two administrators from
suspending one another using stale grants. It serializes administration per Organization,
without an Organization-wide business-operation gate or changes to ordinary admission.

The target's revision and original Organization are native concurrency predicates. Target
reads/writes are scoped by admitted Organization even though Access is a global registry.
The public context is not a general tenant-filtered business context. Audit storage is also
global; any future audit query must explicitly select Organization and enforce visibility.

Existing Gloaming audit storage/staging is installed through explicit mapping and both native
save validations. MembershipAudit selects a V1 payload with stable user, prior/new status
and revision. The envelope records administrator, admitted Organization, membership ID and
time, with no private Personnel content or invented initiator. No-op, conflict and denied
actions create no accepted-change audit. The library cannot infer an omitted business audit
or prevent privileged SQL; a deferred database test oracle independently checks this journey.

Failed saves/cancellation/known rollback leave neither transition nor accepted audit. Dispose
the failed scope and make a fresh decision. No external publication, automatic retries,
grant cleanup, workflow cancellation or identity deletion is performed.

The lock/invariant covers the supported Contract. Direct SQL and future provisioning, grant
revocation, membership removal/transfer or administrator assignment paths must join the same
Organization protocol before claiming the last-admin/authority guarantee. A checked permission
does not fence arbitrary later external SQL. No universal latest-permission-at-business-commit
guarantee follows from this consumer implementation.

## Admission, history and deferred work

Fresh protected human operations reuse ResolveMemberOrganizationAsync's persisted Active
lookup. Suspension of one Organization preserves global login/user links and other active
Organizations. An already-admitted operation keeps its immutable actor/tenant context;
the next fresh admission denies. Public catalog routes remain deliberately public.
Reactivation updates the same membership, never re-creates identities or removed history.

Company-owned work may continue under an explicitly authorized system identity with its
human initiator retained as attribution. A deferred operation requiring continuing human
authority must admit that person afresh and stop visibly when suspended. The proof exercises
these two consumer policies; Access provides no background-work scheduler/authority engine.
Reminder recipient/content checks and manual responsibility reassignment remain module policy.

## HTTP adoption and migrations

[HttpIdentityDemo](../../Api/README.md#membership-administration) exposes native
authenticated point-read and antiforgery-protected POST intents after existing admission.
The forward MembershipAdministration migration adds revision=1, grant=false and audit storage.
It preserves populated pre-administration identities/memberships and grants no existing user
administration authority. Explicit finite demo seeding grants Alpha administration in North
and Beta administration in South; Alpha's South membership demonstrates a separate target.
Normal startup never migrates/seeds. Real provisioning remains explicitly consumer-owned.

No Personnel, general business permissions, license/Organization-availability model or
permission-aware exports/AI queries are implemented here. Reactivation is not a bypass for
those future gates. See the [implementation scope](../../../../docs/plans/a1-1-membership-suspension.md)
and [fresh proof report](../../../../docs/reports/a1-1-membership-suspension.md).
