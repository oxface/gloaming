# E9 Explicit transactional audit

Historical adapter setup: [the later retirement](../reports/thin-adapter-retirement.md)
replaces the convenience package with native consumer configuration. The mechanism and
evidence below retain their original scope.

Date: 2026-10-09. Branch: `feat/explicit-transactional-audit`, based on merged `origin/main`
at `0f4d8bf`. Public interface and bounded scope were owner-approved before implementation.
Implementation is unstaged for line-by-line review; no commit is authorized or created.

## Outcome and ownership

Implemented one reusable mechanism: explicit audit envelope capture, editable native EF mapping,
typed staging and validation of the original context/active transaction and retained envelope.
It requires ActorIdentity values and EF Relational only. The proposed DI-abstractions dependency
was unnecessary for the reviewed API and is omitted. No separate core/host package. The owner-requested optional PostgreSQL package now maps
JSONB through ConfigurePostgresAudit, composing the relational mapping without putting
provider setup in either owning DbContext.

Sales stages accepted profile audit before the second save in its existing native transaction.
It requires a trusted Human actor and retains customer/address identity and version transition,
excluding name/address content. Inventory stages accepted inbox stock-issue audit under its
receiver-owned System identity; header/facts/inline state/audit/reply/completion share the existing
processor transaction. The executable inbox journey prints committed action/actor and reply
identity/cause; the audit itself contains no correlation, causation or trace fields.
Its current wire contract supplies no trusted human initiator, so initiator remains absent.

Classification, operation completeness, trust, authorization, detail disclosure, queries/indexes
and retention stay consumer-owned. There is no automatic audit for direct Inventory commands,
seeds, refusal replies, duplicate intake or replay/rebuilding. No new module Contract or wire
schema is introduced. T1 remains unchanged; no materialized audit template is proven.

## Owner feedback implemented

Business handlers now call scoped consumer-owned wrappers:

```csharp
// Sales: its existing transaction remains explicit.
audit.ProfileChanged(customer.Id, address.Id, change.ExpectedVersion, customer.Version);

// Inventory: existing inbox processor owns save/completion/commit.
audit.StockIssued(command.StockPositionId, accepted.Proposed.Version, command.Quantity);
```

SalesAudit and InventoryAudit fill established ActorIdentity/Tenancy context, UUIDv7 ID,
clock, module source, accepted classification/schema and minimal payloads. The owner
subsequently removed CorrelationId, CausationId and TraceId under YAGNI. Their constructor
arguments, row properties, mappings, audit migration columns and wrapper capture are removed.
No replacement operation IDs are inserted into Details. Sales needs no Activity access or
generated conversation; Inventory's wrapper no longer depends on incoming-message metadata.
This simplification re-verifies the E9 mechanism and proves no additional reusable mechanism.

Item identity and versioned business payload still support native activity timelines and the
required-audit proofs. The Inventory deferred commit oracle now matches audit and outgoing
reply by tenant, item and business version/quantity, with reply causation remaining in its
messaging envelope. Its negative control rejects completion without expected participants.
The HTTP commit oracle continues to verify every selected profile version transition.

The synthetic conversation/distinct-worker-traces audit proof and trace-specific envelope
validation/tampering cases are removed. The finite broker journey no longer installs a local
listener or starts an activity solely for audit. Existing native HTTP telemetry coverage
remains separate; distributed HTTP/message span continuity is a separate
[proposal](../plans/durable-message-observability.md), not an E9 guarantee.

StockIssueMessages now generates conversation correlation per direct-command operation
scope instead of using the aggregate ID. Inbox replies retain incoming correlation even
when absent. The native outbox retry proof checks the new correlation is distinct from item
identity and stays unchanged with message identity/payload across retries. This is sample
policy, not a new Messaging API, wire schema or library default. Actor trust/authorization
stay explicit; worker span IDs do not replace message identity.

The further owner review moves the existing tenant/type/item/time/ID index into ConfigureAudit
as the default ix_audit_subject_timeline. ConfigurePostgresAudit inherits it, and no separate
public indexing helper remains. Both modules need no indexing call; their runtime index shape
and migrations are unchanged. The independent consumer uses a custom audit ID column; its
existing timeline proof checks the actual default PostgreSQL index before querying the item
history. Native EF index customization remains available. PostgreSQL is the exercised provider.

Sales and Inventory wrappers use named V1 Details records whose schema constants live beside
the fields. Existing payloads remain byte-shape compatible; business callers supply no schema
version. Version compatibility and future readers remain consumer decisions. No schema
registry, hash-derived version or automatic audit upcaster is added. TenantKey is now the
last optional AuditEntry argument, after the existing optional ReasonCode. Envelope and row
properties follow the same order. The independent native commit proof retains a customer-request
reason; both sample wrappers leave it absent. Reason vocabulary remains consumer-owned.

The inbox already composes AddPostgresInbox, AddPostgresInboxProcessor and AddInboxHandler.
ActorIdentity's package-free core has no DI helper; its HTTP helper additionally installs
HTTP resolution/policy behavior. The worker's native same-instance accessor aliases and
audit registration stay explicit, without adding dependencies merely for a registration
wrapper. AuditStageRegistry comments and the library guide now explain per-row weak-key
validation evidence, exact native transaction association and managed reference lifetime.
The registry implementation is unchanged; event contract registration remains strong native
lookup by durable name/version. This refinement proves the shared index setup, not an event
registry, transaction cache or broader schema-management mechanism.

ConfigurePostgresAudit in Gloaming.Auditing.EntityFrameworkCore.Postgres specializes Details
as JSONB. Its only dependencies are the EF audit package and Npgsql EF provider. Generic EF
mapping and staging remain free of the provider, tenancy, events, messaging and HTTP.
Module mappings retain their own ownership and native subject timeline index.

SubjectKey is now a nullable opaque item/aggregate ID; SubjectType stays required. Native
proofs retain a global/system entry with a null item and show a type/item-filtered timeline
excludes global/other-item entries, sorting timestamp ties by entry ID. The sample's index
covers tenant/type/item/time/ID. No UI, endpoint or audit query service is introduced, and
this display order does not claim database commit order.

AuditRecord remains a reference-identity EF class. Record equality does not compare
JsonElement content automatically. New proofs accept independently backed identical JSON
text and reject equivalent JSON with changed formatting; the exact pending-envelope guard
is preserved. The existing outbox guard has the same reason for explicit raw text comparison
and remains unchanged. An internal scalar/raw-JSON snapshot record is a possible future
refactor; no broader equality abstraction is proven or extracted.

The two new uncommitted audit migrations/snapshots are revised in place for the nullable item
column and timeline index, with the three operation-ID columns removed. Existing merged migrations and
preserved data are unchanged.
Populated pre-E9 upgrades pass. Databases that applied an earlier local E9 review draft need
local reset/reconciliation; this slice does not ship a second migration for that draft.
[Primary-source research](e9-audit-context-research.md) explains native tracing, EF/JSON equality
and OWASP/NIST guidance. It is research, not compliance certification or executable evidence.

## Latest review refinement verification

Fresh executions after moving the timeline index into ConfigureAudit and retaining the
optional reason passed **411 cases**, with no failures or skips. The independent PostgreSQL
proof checks the default index after native ID-column customization, and the native commit
proof persists customer-request as ReasonCode while rollback/disposal still leave no rows.
Sales and Inventory retain their existing index shape; both populated-upgrade/model checks
pass without migration changes. The original-transaction registry and its replacement-transaction
proof remain unchanged. No additional reusable mechanism beyond E9's mapping/staging was proven.

| Latest refinement check | Result |
| --- | --- |
| Independent AuditPostgresTests | 39 passed. |
| Wholesale HTTP consumer suite | 105 passed. |
| Wholesale event consumer suite | 189 passed. |
| Architecture and intended dependencies | 78 passed. |
| Active solution build | Passed, 0 warnings/errors. |
| Native style/analyzers and CSharpier | Passed; CSharpier checked 528 files. |
| Archive checksum | All 800 retained original files unchanged; archived tests were not rerun. |
| Documentation and Git review state | 240 relative links across 11 documents resolve; diff whitespace check passed; staged snapshot unchanged. |

The owner's review-staging preference is now recorded in global user guidance, rather than
repository development conventions. Repository guidance drops the historical archive-review
exception and personal tooling rationale, retaining technical scripting conventions and the
exact-change-set commit approval gate. No staging, unstaging or commit accompanied this refinement.

## Previous verification checkpoint

Executions before the latest default-index and reason refinements passed **411 cases**,
with zero failures or skips. The preceding YAGNI checkpoint also passed 411 cases; these
runs verified the then-optional shared index helper and named V1 payloads.
These runs include new E9 proofs and existing regression coverage; they are separate from the
three pre-implementation baseline rollback cases and historical archive evidence. The earlier
implementation passed 409 cases, then 413 after contextual/provider/nullable-subject refinements;
a subsequent identifier-separation draft passed 417 cases. Those are historical checkpoints,
not results for the simplified current implementation.

| Previous refinement check | Result |
| --- | --- |
| Independent AuditPostgresTests | 39 passed, 0 failed/skipped; nullable subjects, native custom timeline index/query, exact JSON and native staging/save guards. |
| Wholesale HTTP consumer suite | 105 passed, 0 failed/skipped; named V1 accepted-change payload, fault, required-audit, actor, cancellation, populated Sales upgrade and HTTP telemetry regressions. |
| Wholesale event consumer suite | 189 passed, 0 failed/skipped; 14 inbox/audit/broker cases with named V1 payload, populated Inventory upgrade and outbox/rebuilding/append regressions. |
| Architecture and intended dependencies | 78 passed, 0 failed/skipped; includes optional provider layering. |
| Active solution build | Passed after current index/payload/argument refinements with 0 warnings/errors. |
| Native style/analyzers and CSharpier | Passed native solution style/analyzers; CSharpier checked 528 files. |
| Archive checksum | All 800 retained original files verified unchanged; no archived tests were rerun. |
| Documentation links and git review state | All 296 relative links across 13 reviewed documents resolve; diff whitespace check passed; index empty. |

Reproduction uses the repository's native commands. Local resource limits require
`DOTNET_PROCESSOR_COUNT=2`; the solution build used `--disable-build-servers -m:1 --no-restore`.
Test projects are `src/Gloaming.Auditing/tests/AuditPostgresTests/AuditPostgresTests.csproj`,
`samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj`,
`samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj` and
`tests/ArchitectureTests/ArchitectureTests.csproj`. Run `dotnet test --project <path>` with
the existing native no-progress/output flags. After a successful build, `--no-build --no-restore`
avoids concurrent compilation; the refinement suites used the completed solution build. Logs are summarized here rather
than treated as shipped artifacts.

The first simplification event run passed 188 cases and failed the required-audit negative
control because the revised SQL referenced `o.tenant_key` instead of the native outbox
`owner_key` column. The control asserted the intended rejection message and caught the
malformed proof. After correcting the query and rebuilding, the full 189-case suite passed.
The failed draft is not counted as a successful execution.

Concurrent full-suite builds exhausted the local process limit, causing native build/copy and
container-startup failures. Verification uses bounded build parallelism followed by built test
executables; those infrastructure failures are not counted as new audit proof. Migration
scaffolding also required ordinary file-scoped-namespace/static-array style corrections.

## Proof intent and remaining limits

The independent ordinary EF consumer exercises custom schema/table/ID mapping, tenantless
Human/System/Anonymous attribution, optional initiator, owned JSON and native begin/save/commit.
It checks cross-connection invisibility before commit, multiple saves, rollback/disposal,
audit/business faults, cancellation and fresh recovery, duplicate ID, missing/ended/replaced
transactions, context transfer and guarded tracked inserts/edits/deletes. The disposable fixture
uses EnsureCreated; the real modules exercise consumer-owned forward migrations. Sales upgrades
populated InitialSales rows without changing name/address/version. Inventory upgrades populated
I1 tables while preserving facts/current state, an outgoing reply and pending incoming work;
the retained incoming work then commits its first audit and completes successfully.

Consumer-specific deferred constraints independently reject commit without the expected audit.
Negative controls assert the intended rejection message, ensuring malformed test SQL cannot
masquerade as a required-participation proof. Sales checks profile versions; Inventory checks
accepted audit/reply against incoming subject/version/quantity. The Inventory fault matrix covers
header, event, inline state, audit, reply and inbox completion separately, including rollback
after earlier SQL writes and fresh processing recovery.

Staging/save validation does not infer missing audit calls, certify detail truth, prevent raw/bulk
SQL or supply tamper evidence. Explicit native atomicity requires the same owning transaction.
Failed/canceled operations require rollback/disposal and a fresh context; a lost commit response
may be ambiguous. Retention, denial-audit durability, actor propagation, other providers and
cross-module commits remain deferred. No new generalized workflow, authorization or retention
mechanism is proven by this slice.

## Review-worthy files

- [Public envelope](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/AuditEntry.cs),
  [typed interface](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/IAudit.cs),
  [staging implementation](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/EfAudit.cs),
  [provided row](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/AuditRecord.cs),
  [mapping/save guard](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/AuditModelExtensions.cs)
  and [internal evidence registry](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/AuditStageRegistry.cs).
- [PostgreSQL helper](https://github.com/oxface/modulith-foundry/blob/1582e77849088e243881c82a37c0a42b2a11a322/src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore.Postgres/PostgresAuditModelExtensions.cs),
  its optional project/dependency declarations and [provider setup](https://github.com/oxface/modulith-foundry/blob/1582e77849088e243881c82a37c0a42b2a11a322/src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore.Postgres/README.md).
- [Sales wrapper](../../samples/Wholesale/modules/Sales/Sales/SalesAudit.cs) and
  [Inventory wrapper](../../samples/Wholesale/modules/Inventory/Inventory/InventoryAudit.cs);
  [Human consumer](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs) and
  [worker consumer](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueInboxHandler.cs),
  their module registrations/DbContexts, StockIssueMessages, the finite InboxJourney and two
  new forward audit migrations/snapshots.
- [Independent proofs](../../src/Gloaming.Auditing/tests/AuditPostgresTests/TransactionTests.cs),
  [Sales proofs](../../samples/Wholesale/IntegrationTests/Api/ProfileAuditTests.cs) and
  [Inventory proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/InboxDispatchTests.cs)
  and [outbox retry proof](../../samples/Wholesale/IntegrationTests/EventPersistence/OutboxDispatchTests.cs).
  Two supporting older Sales migration assertions now compare all declared migrations.
- [Library-local contract](../../src/Gloaming.Auditing/docs/transactional-audit.md),
  [reviewed scope](../plans/e9-explicit-transactional-audit.md) and
  [ADR 0011](../adr/0011-explicit-transactional-audit.md); architecture/CI/hook integration.

The completed executions prove a new reusable mechanism: explicit envelope capture and
context/transaction-associated tracked audit staging/save validation, independently adopted
and exercised in Human and System consumer workflows. Automatic completeness detection and
tamper-evidence claims are rejected; native queries/migrations and product classification remain
consumer setup/policy. No new template composition or general utility is proven. Archived
audit/receipt patterns and their historical totals remain evidence for comparison only;
archived source and fixtures are preserved.
