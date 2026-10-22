# A2.9: admitted Sales reservation replies under RLS

Implemented for owner review on 2026-10-17 after A2.8 checkpoint `087a008`, on the existing
`feat/sales-postgres-rls` branch. The owner selected the [bounded inbox slice](../plans/a2-9-sales-inbox-rls.md).
The [consumer guide](../../samples/Wholesale/Worker/Broker/sales-inbox-rls.md) gives setup, execution,
the operation scope and exact persistence limitations. All new changes remain unstaged.

## Outcome and decisions

The existing global inbox processor begins its transaction before locking/decoding a message;
the original handler establishes tenancy afterward. Installing the current RLS interceptor on
that composition would reject the transaction before a tenant exists. A2.9 uses an explicitly
admitted Organization per worker process instead: every native worker scope automatically gets
its immutable tenant and receiver-owned system actor before transaction start. The existing
processor, interceptor, row lock, save/commit and retry transactions remain unchanged.
No late GUC mutation, adopted transaction, extra transaction owner or new Gloaming interface is needed.

The selected WF2 reservation continuation supplies real effects and outgoing work to prove atomicity.
`AddSalesReservationReplyDatabase` chooses a stateless native EF model customizer after ordinary
capability registration. Its workflow/inbox ownership declarations select automatic local context;
query filters, concurrency and save guards remain. The module's admitted reply entry requires
the established tenant/system actor to match. The existing message-established handler still
establishes its own operation and delegates to that same continuation logic. No business rule is
copied into the host or moved into a technical library.

The consumer roles are explicit one-shot `setup-sales-replies` and long-running
`process-sales-replies`. Setup creates a restricted login and policies/grants against already
migrated tables. Processing needs only that runtime connection plus an admitted Organization;
it rejects known retained broker/business/provisioning credentials before connections open.
Each instance covers Alpha or Beta, and replicas may compete within that partition. There is
no tenant discovery, dynamic scheduling, global claim/bootstrap privilege or actor rebinding.
The receiver-owned system mandate continues retained work without rechecking an initiating
human's membership; no human identity/authority is inferred from message attribution.

Final review rejected leaving queue INSERTs protected only by tracked guards. That would permit
foreign raw enqueue writes. The optional setup now installs separate outbox command policies:
tenant-equal INSERT WITH CHECK, global SELECT and global delivery UPDATE. Object/column grants
separate appending from dispatch. A2.8 still supports an unprotected queue; its current verifier
also recognizes this explicit policy inventory. Its runtime still has no INSERT or business rights.
The processor can append and read generated timestamps, with no payload read or delivery update.
One native transaction still commits the continuation, next command and delivery completion.

## Protection and limitation matrix

| Boundary | Fresh protection/proof | Limitation |
| --- | --- | --- |
| Operation admission | Configured Alpha/Beta checked before connections; fresh immutable tenant and exact receiver-owned system actor. Foreign envelope/human/other-system entry rejects without rebinding. | Finite trusted host configuration, not tenant authentication, Access membership or dynamic tenant discovery. One configured tenant per instance. |
| Native transaction | Existing processor begins it; existing interceptor automatically initializes its local GUC before raw claim SQL. | Selected reads also require a transaction. Credential holders can forge the mutable GUC; pooling/failover modes outside the existing adapter contract remain unsupported/unproven. |
| Workflow rows | ENABLE/FORCE USING/WITH CHECK on existing `TenantKey`; disabled EF filters, raw SQL and bulk writes remain tenant-scoped. Foreign owner update rejects with 42501. | RLS does not impose workflow-type/field permissions within a tenant. The physical nullable column is unchanged; tenantless stored rows are excluded. |
| Inbox lifecycle | ENABLE/FORCE restricts claims, completion and retry. Only SELECT plus completion/availability UPDATE granted. | No intake INSERT or envelope mutation. Same-tenant subscriptions remain consumer handler/admission policy; poison/unknown-tenant rows need operator policy. |
| Outbox appends | Actual native INSERT succeeds for the matching transaction tenant; missing/foreign contexts fail with an RLS error, including outside EF's tracked guards. | Timestamp columns/counts are globally readable under the necessary SELECT grants; payloads are denied to this role. No exactly-once publication promise. |
| Global delivery | Real A2.8 restricted dispatcher works with the new command policies and no tenant context/BYPASSRLS. It cannot INSERT. | Global payload read and delivery-state authority are explicit transport policy. Compromised dispatcher/broker privileges remain a separate boundary. |
| Atomic effects | Workflow save, outgoing INSERT and inbox completion failures each roll back all participants; fresh scopes retry. | Commit responses can be ambiguous. Retries retain original message/workflow identities and current native protocol. |
| Competing/restarted workers | Real processes skip locked work and commit each continuation once. SIGTERM and hard death at blocked completion roll back effects; restart succeeds. | Correctness evidence, not throughput, fairness, tenant scheduling or leader election. |
| Runtime role | Actual non-owner/non-superuser/non-BYPASSRLS login; no memberships, administrative rights, other application-relation access or grant options. Policy/grant faults and PUBLIC column leaks reject. | Connected-database finite catalog checks, not a full audit of other databases, routines/SECURITY DEFINER paths, extensions, trigger bodies or later grant changes. |
| Provisioning/startup | No runtime DDL, seed, role creation or broker dependency. Empty-storage setup fails without schema/login creation. Role/policies/grants install in one native transaction. | Post-commit verification may fail after setup persisted. Existing role/policy lifecycle, rotation, upgrades, restrictive CONNECT grants and production secrets remain operator-owned. |

Startup checks role authority, ENABLE/FORCE, exact policy names/commands/roles and explicit clause
presence; global outbox read/update expressions must be true. They do not certify arbitrary
replacement tenant predicates or privileged changes after startup. The reviewed installer and
the real row/write proofs establish the tested equality behavior. Owners must review deployed
tenant predicate bodies, not infer complete certification from successful startup.

## Primary-source grounding

Native [EF ConfigureDbContext](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.configuredbcontext?view=efcore-10.0)
applies configurations in registration order, allowing this concrete consumer model without a
new registry/interface. The installed EF 10.0.12 XML reference confirms the same contract.
[PostgreSQL row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html) defines per-command
policies, USING/WITH CHECK, default denial and privileged exceptions. That supports tenant-checked
enqueue alongside explicit global delivery policies. The existing raw processor uses native
[FOR UPDATE SKIP LOCKED](https://www.postgresql.org/docs/18/sql-select.html#SQL-FOR-UPDATE-SHARE)
inside one transaction. [PostgreSQL GRANT](https://www.postgresql.org/docs/18/sql-grant.html) supplies
the independent table/column rights, including the distinction between ownership and ordinary grants.
The optional [adapter contract](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
retains its transaction-local context, immutable scope and unsupported-mode obligations.

## Verification

Fresh checks from this checkout:

| Suite / check | Result |
| --- | --- |
| Complete workflow suite | **139 passed**, zero skipped, in 1m55s: 47 new A2.9 cases, two additional reverse-credential guards for A2.8 and 90 retained cases. |
| Related local-delivery suite | **14 passed**, zero skipped, including the retained shared Sales handler path. |
| Architecture | **96 passed**, zero skipped; boundary rules unchanged. |
| Full solution build | Passed with zero warnings/errors using `--no-restore`. No dependency/audit configuration changed; no fresh audited restore or dependency security attestation. The prior MessagePack advisory/workaround state recorded in A2.6 is unchanged. |
| Full active style / analyzers | Both verify-only checks passed without modifying files. |
| CSharpier / frozen archive | 810 files checked; all 800 original archived files preserved. |
| Documentation / whitespace | 412 local links/anchors across nine changed Markdown documents resolved; changed-file whitespace checks passed. |

**249 tests passed**, counting only the final relevant suite runs. All 23 new/changed files
remain unstaged; the index and staged diff match their pre-edit snapshots. No commit or push
is included in this implementation handoff.

The real-broker proof uses PostgreSQL 18.6 and RabbitMQ 4.3.6, actual typed Inventory processing,
restricted Sales dispatch and a restricted Sales processor child process. Both reservation
steps complete at workflow version 3, with two committed/processed replies and two dispatched
commands. Separate Alpha/Beta proofs leave the other tenant unchanged and verify fresh immutable
contexts. Raw/bulk proofs remove EF filters; transaction-local context clears after commit and
rollback with the same connection still open. The fixture's reset-enabled pool has maximum size
one. Participant-failure tests verify retry metadata and empty context after rollback, then retry
with a fresh scope and controlled database availability timestamp.

Native triggers hold actual in-flight workflow updates or completion, observed through pg_locks.
Two process instances prove SKIP LOCKED progress. SIGTERM/hard death occur after workflow/outbox
proposals have been saved but before completion can commit; another connection sees none of those
uncommitted effects. Restart commits one continuation/next command/completion. No arbitrary sleep,
mock transaction, test-only runtime switch or relaxed assertion supplies these boundaries.

An initial model expression captured a local context variable; the ownership contract requires a
direct DbContext member. The final model builds that exact native expression explicitly. Compilation
also caught using placement and an interpolated raw-SQL proof; both were corrected, and raw SQL
uses parameters. Intermediate smoke/core/full passes are not added to final totals. The earlier
136-case full pass preceded the additional database outbox-write protection; the final run includes
all three native enqueue context cases and global-dispatch coexistence.

## Extraction and review

**Library:** no new reusable mechanism was proven or introduced; no Gloaming code, public interface,
dependency, core Tenancy-to-Access/FGA coupling or transaction ownership changed. Existing native
composition is sufficient. **Sample:** finite admission/scoped factories, public module composition
methods, the admitted handler entry, model selection, credentials, grants and command policies stay
consumer-owned. The private queue-policy helper has two real consumer callers; it is not a library
registry or provider abstraction. **Template:** no output/preset changed. Existing CI already covers
the affected workflow/local consumers and architecture suites; no project/dependency/lane changes.

Review-worthy files:

- [Worker role composition](../../samples/Wholesale/Worker/Broker/BrokerHost.cs),
  [scoped admission](../../samples/Wholesale/Worker/Broker/SalesReplyProcessing.cs),
  [finite setup/grant checks](../../samples/Wholesale/Worker/Broker/SalesReplyDatabase.cs),
  [queue policies](../../samples/Wholesale/Worker/Broker/SalesOutboxPolicies.cs) and
  [dispatcher compatibility](../../samples/Wholesale/Worker/Broker/SalesDispatchDatabase.cs).
- [Sample model selection](../../samples/Wholesale/modules/Sales/Sales/SalesReservationReplyDatabase.cs),
  [module registrations](../../samples/Wholesale/modules/Sales/Sales/SalesRegistration.cs),
  [shared admitted/default handler](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationReplyHandler.cs)
  and [receiver-owned actor declaration](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationReplyAdmission.cs).
- [Atomicity/isolation proofs](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesReplyTests.cs),
  [process/broker recovery](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesReplyTests.Recovery.cs),
  [startup controls](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesReplyTests.Startup.cs) and
  [fixture](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesReplyProof.cs).

This closes the selected bounded HTTP/dispatch/WF2 processing demonstration once owner-reviewed.
It does not certify every application table, Inventory runtime, WF1 worker, broker receiver or
production deployment. Broader coverage, tenant scheduling, restricted broker intake, default
rollout, role lifecycle/rotation, external pooler/failover, policy generation and template output
remain separately selectable rather than automatic next slices. Prior A2.5–A2.8 results are
historical evidence and are not included in the new verification counts.
