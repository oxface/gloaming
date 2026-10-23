# A1.1: authorized membership suspension/reactivation

Implementation authorized by the owner on 2026-10-16 after the
[A1 assessment](access-authorization-assessment.md). Prepared on
`feat/membership-suspension` in `/tmp/modulith-foundry-membership-suspension`, based on
approved assessment `fd8a10e`, following fetched main `7ea6652`. The
[concrete scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a1-1-membership-suspension.md) records consumer Contracts, native
persistence/HTTP adoption and the owner's selected product policies. At the initial
implementation handoff, changes were unstaged and commit approval remained separate.

## Outcome and selected policies

Access now exposes consumer-owned point read, suspend and reactivate operations through
`IMembershipAdministration`. Trusted human actor and selected Organization come from the
existing contexts. Access rechecks current active membership and its narrow persisted
membership-administration grant for every call, including retries and no-ops. Foreign,
missing and unauthorized targets have one non-disclosing result. No grant-editor/general
role model is added, and this grant supplies no business-module permission.

The owner selected blocked self-suspension, preservation of an active membership
administrator, and retained grants becoming eligible again under current permission checks
after reactivation. Removed memberships are not reactivated. Global users, linked external
identities, historical membership rows and other Organizations remain intact.

One native ReadCommitted transaction owns the administrative command's save/commit. A native
Organization row lock precedes current authority/target reads, serializing this narrow
administrative capability. Membership revision plus original Organization protect the update
through native EF concurrency. Competing administrators cannot both suspend each other from
old authority. This is consumer policy/protocol, not a general lock or authorization engine.

The accepted transition increments revision once and stages one Gloaming accepted-change
audit in that same Access transaction. Its V1 details contain stable target user, prior/new
status and revision; its envelope carries Organization, membership, actual administrator and
time. No private Personnel content, invented initiator, trace IDs or denial audit is added.
Audit mapping and both native save validations are explicit in Access's global context.

Current-revision equal-state commands are no-ops; stale or ABA revisions conflict. A duplicate
old request does not add another transition/audit. There is no command receipt: a lost commit
response can mean status/audit already committed, and a fresh authorized state read supports
reconciliation without asserting which attempt caused a change. No automatic retry occurs.

Native authenticated point-read and antiforgery-protected suspend/reactivate routes adopt
these Contracts. Existing native authorization → actor → active-membership admission → tenant
publication order remains. The original persisted admission query is unchanged. Suspended
admission denies the same issued cookie on subsequent protected operations while global
identity/other active Organizations remain usable. Already-admitted operation context stays
immutable; the existing Removed case now also covers Suspended. Public catalog exceptions
remain deliberate.

The forward migration preserves the pre-administration schema/data, adds revision=1 and
grant=false, and creates Access audit storage. It grants no existing user new privilege.
Explicit finite demo seeding assigns Alpha North administration and Beta South administration;
their memberships remain unchanged. Startup performs no migrations or provisioning.

## New proofs versus historical evidence

The two MembershipTests files define 32 new cases; the existing admitted-operation theory
adds one Suspended case while retaining Removed. They assert independently expected outcomes,
persisted state and real PostgreSQL boundaries, including:

- Same-cookie protected business read/mutation denial after an authorized suspension,
  unchanged North profile/audit/outbox, global identity, usable South read/mutation, retained
  grants after reactivation, and a grant revoked during suspension staying revoked.
- Unauthorized/direct/foreign/missing target behavior, native authentication/antiforgery,
  invalid revisions/IDs and required human/selected-tenant contexts.
- Changed/no-op/stale/ABA/Removed outcomes, no duplicate accepted audits, and current
  administrative reauthorization even when an equal-state request would otherwise be a no-op.
- Controlled concurrent identical/opposing requests and administrators suspending one another;
  barriers observe PostgreSQL blocked locks rather than relying on sleeps to infer concurrency.
- Native revision and original-Organization predicates against an external writer acting
  after the decision read. The latter cannot redirect the status update into a foreign scope.
- Membership/audit/commit failure rollback and fresh recovery, cancellation after save,
  and an injected lost commit response after actual commit followed by fresh-state read.
- A deferred constraint-trigger oracle that first rejects an intentionally omitted audit,
  then checks matching actor, target, statuses and revisions for the actual business journey.
- Preserved removed-membership history and external-identity resolution, and upgrading the
  unchanged original SQL fixture with populated suspended membership without privilege gain.
- Fresh non-HTTP personal admission denies after suspension. A finite explicit company mandate
  still permits the selected system stock reader with its human initiator retained. These
  are two exercised consumer composition policies, not a background-work framework.

E3/E9/A1/C1 results remain separately dated historical evidence. A1 did source inspection;
this slice adds runtime proofs. The first full run exposed three existing tests assuming a
single Access migration. They now verify complete applied histories and the expected new
migration while retaining the original fixture and its identity/admission preservation proof.

## Fresh verification

Executed on 2026-10-16 with .NET SDK 10.0.401, pinned ASP.NET/EF 10.0.12,
PostgreSQL 18.6 Testcontainers and rootless Podman, with resource reaping enabled.
Commands are documented in the [development guide](../development.md#organization-membership-administration).

| Check | Fresh result |
| --- | --- |
| Active solution build | Passed, 84 projects, zero warnings/errors. |
| HTTP/PostgreSQL suite | 138 passed, zero failed/skipped; includes 32 new membership cases and the additional Suspended in-flight case. |
| Architecture suite | 90 passed, zero failed/skipped; module/Contracts/library boundaries retained. |
| Native style and analyzers | Both passed for the active solution. |
| CSharpier | Passed with `--include-generated`, 758 files checked. The commit hook exposed formatting in two generated migration metadata files that the earlier 723-file check skipped; their formatting was corrected without changing non-whitespace content. |
| Frozen archive | All 800 original files match the retained manifest. |
| Documentation/whitespace | 464 local links/anchors resolve across 11 changed documents; explicit whitespace checks cover all 31 changed tracked/untracked files, and git diff checks pass. |
| Review snapshots | Edits were unstaged at the implementation handoff. Original checkout status/HEAD/index and other recorded worktree index hashes are unchanged; the approved staged snapshot was preserved while correcting generated-file formatting in the working tree. |

Commit hooks also reran the selected Auditing, ActorIdentity, Tenancy, Persistence, Events
and EventSourcing regressions; all passed. No FGA/RLS, browser-provider deployment or
performance benchmark was executed for this slice. Existing HTTP process/setup proofs were
rerun within the 138 cases. No archive fixture, runtime library source or template was changed.

## Review-worthy files

| File | Review concern |
| --- | --- |
| [Consumer Contract](../../samples/Wholesale/modules/Access/Access.Contracts/IMembershipAdministration.cs) | Narrow intent/point-read surface, stable statuses/revision and explicit business outcomes; no technical context dependency. |
| [MembershipAdministration](../../samples/Wholesale/modules/Access/Access/MembershipAdministration.cs) | Trusted scope, current authority, Organization serialization, revision/ownership predicates, chosen safeguards, no-op/conflict semantics and explicit transaction. |
| [MembershipAudit](../../samples/Wholesale/modules/Access/Access/MembershipAudit.cs), [context](../../samples/Wholesale/modules/Access/Access/Persistence/AccessDbContext.cs) | Accepted-change selection, safe V1 details, global registry/audit mapping and sync/async validation. |
| [Forward migration](../../samples/Wholesale/modules/Access/Access/Persistence/Migrations/20261015212953_MembershipAdministration.cs) | Defaults grant no privilege, retain data and add native revision constraint/audit storage; updated generated metadata includes original Organization concurrency. |
| [HTTP bridge](../../samples/Wholesale/Api/Endpoints/MembershipEndpoints.cs) | Native authentication/antiforgery, no actor/Organization override, concealment and response mapping. |
| [Journey cases](../../samples/Wholesale/IntegrationTests/Api/MembershipTests.cs), [transaction cases](../../samples/Wholesale/IntegrationTests/Api/MembershipTests.Transactions.cs), [admission](../../samples/Wholesale/IntegrationTests/Api/AdmissionTests.cs) | Independent state/audit expectations, real concurrent/commit boundaries, stable identities and non-HTTP work policy. |
| [Access guide](../../samples/Wholesale/modules/Access/README.md), [HTTP guide](../../samples/Wholesale/Api/README.md#membership-administration) | Consumer setup, permission/retry/transaction ownership and supported limits. |

## Extraction findings and remaining gaps

**No new reusable mechanism was proven or added.** Existing contexts, native EF transactions,
PostgreSQL locks/concurrency and Gloaming audit compose this journey. No library source or
public library interface changed. Simple repeated mechanics can still earn later extraction
when actual adoption removes enough work; this result does not reject such opportunities.

**Consumer policy:** the narrow admin grant, last-admin/self-suspension rules, transitions,
retained-grant behavior, concealment, accepted-audit details and work mandates belong to Access
and Umbrine. Direct SQL can bypass the business Contract. Future grant/removal/provisioning/
transfer paths must join this same Organization protocol before inheriting its invariants;
target concurrency alone does not fence an independently changed administrator grant.

**Template:** editable consumer registration, native HTTP bridge, audit setup and explicit
fresh-scope/admission examples are exercised candidates. T1 generation is unchanged. Access
now depends explicitly on the existing ActorIdentity/Tenancy/audit libraries as a consumer;
none of those libraries gains a dependency on Access or on each other through this slice.

**Sample/product limits:** known target IDs are required; member discovery, administrator
provisioning/grant editor, invitations and Removed/reinvitation remain separate capabilities.
Personnel history and its identity-link model are not present; no new Personnel lifecycle is
invented. Licensing, Organization/capability availability, general business-resource grants,
private-data filtering/export/AI selection and configurable roles remain A1 follow-ups, not
guarantees of membership administration. No automatic offboarding/reassignment, reminder
delivery policy, global logout, customer IdP management, RLS or FGA is introduced. A2 remains
separate; its admission/query/isolation boundaries and optional RLS proofs are unchanged.

No new production-scale query/lock benchmark, external provider/browser deployment journey,
strict resource-permission-at-commit protocol, durable operation receipt or tamper evidence
is claimed. Authenticated dependency faults remain native errors, not deny or success.
