# E9 Explicit transactional accepted-change audit

Status: public interface and bounded scope owner-approved for implementation, 2026-10-09.
Branch: `feat/explicit-transactional-audit`, created from refreshed `origin/main` at `0f4d8bf`.
The owner subsequently instructed proceeding with implementation. The capability is implemented
and verified; [the execution report](../reports/e9-explicit-transactional-audit.md) records the
current-implementation test cases and remaining limits. All changes remain unstaged;
implementation approval and exact-change-set commit approval are separate.

## Owner-requested refinements for implementation review

The owner requested context-enriched sample calls, PostgreSQL mapping separation and nullable
item/aggregate identity. The implementation now includes these bounded changes:

- AuditEntry's constructor parameter and both envelope/row SubjectKey properties are string?.
  Null means no individual item; non-null blank keys remain invalid. SubjectType stays required.
- Optional `Gloaming.Auditing.EntityFrameworkCore.Postgres` references the EF package and
  Npgsql.EntityFrameworkCore.PostgreSQL. Its only public type is:

```csharp
namespace Gloaming.Auditing.EntityFrameworkCore.Postgres;

public static class PostgresAuditModelExtensions
{
    public static EntityTypeBuilder<AuditRecord> ConfigurePostgresAudit(
        this ModelBuilder model, string schema, string table);
}
```

It composes ConfigureAudit, its default timeline index and JSONB details mapping. It supplies no ownership policy,
clock default, transaction orchestration or mandatory provider dependency in the EF package.

- Internal scoped SalesAudit.ProfileChanged(Guid customerId, Guid addressId, long previousVersion,
  long version) and InventoryAudit.StockIssued(Guid stockPositionId, long version, decimal quantity)
  fill source/action/outcome/schema/payload, established actor/tenant, generated UUIDv7 and clock.
  Neither wrapper needs Activity or incoming-message metadata. Correlation, causation and
  trace IDs are omitted under the owner's subsequent YAGNI decision. No ambient Gloaming
  context is added.
- Sample mappings index tenant/type/item/time/ID for native activity timeline queries. Global
  entries have nullable item keys; timestamp/ID ordering is display order, not commit order.
- AuditRecord remains a class and exact raw JSON comparison remains intentional. An EF entity
  record would not remove the need for explicit JsonElement comparison. The same reasoning
  applies to the existing outbox pending-envelope guard, whose implementation is unchanged.

The two uncommitted AddTransactionalAudit migrations are revised in place: nullable subject
column and timeline index. Existing merged migrations and archived fixtures remain untouched.
This is the first E9 schema delivery; upgrading a database that applied an earlier local E9
review draft is not a supported migration path. Normal populated pre-E9 upgrades are tested.
[Primary-source research](../reports/e9-audit-context-research.md) supports these choices;
implementation and exact-change-set commit review remain separate.

## Owner-requested YAGNI simplification

The owner removed CorrelationId, CausationId and TraceId from AuditEntry/AuditRecord,
constructor arguments, mapping and both first-delivery audit migrations. No identifier
replacement is introduced in Details. SalesAudit no longer creates a conversation or reads
Activity; InventoryAudit no longer depends on incoming-message context. Existing subject
type/key and native timeline index support item activity history. Messaging retains its
existing correlation/causation fields independently.

The finite broker journey no longer creates a local tracing listener/activity solely for
audit. It queries audit by stock identity and prints committed action/actor plus reply metadata.
The Inventory required-audit commit oracle matches business item/version/quantity and the
reply to its incoming command; its negative control still rejects omitted audit participation.
Trace-field and synthetic conversation/trace audit proofs are removed. Native HTTP telemetry
proofs remain separate. Distributed HTTP/message tracing is a separate
[proposal](durable-message-observability.md), not an E9 guarantee.

The two uncommitted audit migrations are first-delivery drafts revised in place. Merged
messaging migrations and wire/Contracts are unchanged.

## Further owner interface and sample refinements

TenantKey is now the final optional AuditEntry constructor parameter, after ReasonCode.
ReasonCode remains an optional business explanation, immediately before TenantKey in the
constructor and envelope/row properties. For example, an accepted stock adjustment could
carry physical-count or damaged-stock as its reason. Consumers own the vocabulary; both
accepted-change sample wrappers currently leave it absent. The independent native commit
proof persists a customer-request reason alongside its document change.

ConfigureAudit now includes ix_audit_subject_timeline on tenant/type/item/time/ID by
default; ConfigurePostgresAudit inherits it. No separate public indexing helper is needed.
Consumers can rename or replace the index through native EF configuration. Both module
models require no indexing call; their model shape and first-delivery migration definitions
are unchanged. The standalone tenantless proof checks the default PostgreSQL index with a
custom ID column alongside the timeline query.

SalesAudit and InventoryAudit now use named V1 payload records with schema constants beside
the fields. The wrappers supply schema/serialization; business callers do not manage them.
Future incompatible payloads/readers remain consumer-owned; no audit event registry or codec
is added. Messaging registration already uses AddPostgresInbox, AddPostgresInboxProcessor
and AddInboxHandler. The remaining actor accessor aliases have no provider-free DI helper;
AddHttpActorContext also installs HTTP resolution/policy behavior and is unsuitable here.
No new DI dependency or package is introduced for these native registrations.

## Actual starting state

The initial working tree and index were clean. Git records outbox PR #1 merged as `beafa4e`
and inbox PR #2 merged as `0f4d8bf`. The inbox implementation is `ecab884`, with CI refinement
`863456b`. Active code already contains typed outbox enqueue/save guards, committed durable
intake, PostgreSQL row-locked local processing, optional workers and executable receivers.
The extraction-plan status and one design paragraph still described inbox as unmerged/next;
this proposal corrects those current-status notes without rewriting historical reports.

There is no active audit library or audit participant in these workflows:

- `Sales.CustomerProfiles.ChangeAsync` owns a native transaction. It saves customer name/version,
  then address, then commits. HTTP supplies trusted application actor, tenant admission and
  native antiforgery. `ProfileTests.cs` exercises success, conflict, partial-write rollback and
  cancellation. The older `PersistenceDemo` has separate E2 consumer types; it is not this module.
- `Inventory.StockIssueInboxHandler` validates receiving policy, establishes tenancy, then calls
  `StockPositionCommands.IssueAsync`. An accepted issue stages facts, inline aggregate state and
  its outbox reply. `PostgresInboxProcessor<InventoryDbContext>` owns save/completion/commit in
  that same transaction. Existing `InboxDispatchTests` cover accepted/refused work, duplicate
  delivery and reply/completion failures. The incoming envelope carries no actor or initiator.

## Archived comparison and extraction decision

Historical evidence only; preserve every archived source, migration and fixture.

| Evidence | Repetition or policy revealed | E9 decision |
| --- | --- | --- |
| [Sales audit entry](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Sales/Sales/Audit/SalesAuditEntry.cs), [Inventory audit entry](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/ReferenceData/InventoryAuditEntry.cs), [Purchasing audit entry](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/Audit/PurchasingAuditEntry.cs) | Repeated identity/time/source/subject/action/detail envelope, JSON ownership and human/system representation. Module factories also select outcomes/reasons and hard-code workflow identities. | Extract envelope capture and explicit EF staging/mapping; leave factories and classifications in consumers. Reuse current ActorIdentity values instead of archived GUID user/system fields. |
| [Access audit entry](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Organizations/AccessAuditEntry.cs) | Same envelope plus optional correlation/trace and explicitly versioned details. | Correlation, causation and diagnostic trace fields omitted under YAGNI; the current business envelope needs no operation context. |
| [Inventory audit mapping](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/ReferenceData/Persistence/InventoryAuditEntryConfiguration.cs) and corresponding Sales/Purchasing mappings | Repeated native column configuration, plus module schema, JSONB, ownership filters and indexes. | Provide editable relational mapping for one row type per context; consumers select schema/table, ownership and indexes; the optional PostgreSQL package maps JSONB. |
| [Human customer creation](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Sales/Sales/Customers/CreateCustomer/CreateCustomerHandler.cs) | Business insert and success audit share native SaveChanges; permission-denial audit is a separate explicit path. | Accepted-change atomicity only. Denial durability/availability semantics need a separate reviewed slice. |
| [Workflow reservation handler](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/Reservations/ReserveStockHandler.cs) | Accepted decision, audit, receipt and outgoing outcome share one transaction; delivery and semantic identity differ. | Prove current stock issue, audit, inbox completion and reply together. Do not copy its reservation model or business idempotency policy. |
| [Customer persistence proofs](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/tests/PersistenceTests/CustomerPersistenceTests.cs) | Audit-insert fault rolls back business effects; a deferred constraint checks required audit at commit. | Transfer both fault and required-participation proof intent to active consumer workflows. Test-only deferred constraints are independent oracles, not automatic library auditing. |

The candidate removes common envelope validation/snapshotting, identity flattening, mapping
and transaction-associated tracked-write validation. Native EF supplies atomic save/commit.
A wrapper around `DbSet.Add` alone would not justify extraction. There is no claim that the
library knows which business change requires an audit or can detect an omitted `Stage` call.
Consumer proofs must establish that these two selected successful workflows actually call it.

## Reviewed public surface

The provider-independent package is `Gloaming.Auditing.EntityFrameworkCore`, under
`src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/`. No separate abstractions or
non-EF package is earned by these consumers. All following types use that namespace.

Dependencies: existing `Gloaming.ActorIdentity` for immutable actor/initiator values;
native `Microsoft.EntityFrameworkCore.Relational`, using the existing central version.
The proposed DI-abstractions reference is omitted because the approved surface needs no library
DI code; native scoped registration remains consumer wiring.
No Tenancy, Persistence, Events, EventSourcing, Messaging, ASP.NET Core, provider, transport or
hosting dependency. PostgreSQL is the first proof provider, not a multi-provider guarantee.

```csharp
public sealed class AuditEntry
{
    public AuditEntry(
        Guid id,
        DateTimeOffset occurredAt,
        ActorContext attribution,
        string source,
        string action,
        string subjectType,
        string? subjectKey,
        string outcome,
        int schemaVersion,
        JsonElement details,
        string? reasonCode = null,
        string? tenantKey = null);

    public Guid Id { get; }
    public DateTimeOffset OccurredAt { get; }
    public ActorContext Attribution { get; }
    public string Source { get; }
    public string Action { get; }
    public string SubjectType { get; }
    public string? SubjectKey { get; }
    public string Outcome { get; }
    public int SchemaVersion { get; }
    public JsonElement Details { get; }
    public string? ReasonCode { get; }
    public string? TenantKey { get; }
}

public interface IAudit<TDbContext> where TDbContext : DbContext
{
    void Stage(AuditEntry entry);
}

public sealed class EfAudit<TDbContext> : IAudit<TDbContext>
    where TDbContext : DbContext
{
    public EfAudit(TDbContext database);
    public void Stage(AuditEntry entry);
}

public sealed class AuditRecord
{
    // EF construction only; public getters with private setters.
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public ActorKind ActorKind { get; private set; }
    public string? ActorKey { get; private set; }
    public ActorKind? InitiatorKind { get; private set; }
    public string? InitiatorKey { get; private set; }
    public string Source { get; private set; }
    public string Action { get; private set; }
    public string SubjectType { get; private set; }
    public string? SubjectKey { get; private set; }
    public string Outcome { get; private set; }
    public int SchemaVersion { get; private set; }
    public JsonElement Details { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? TenantKey { get; private set; }
}

public static class AuditModelExtensions
{
    public static EntityTypeBuilder<AuditRecord> ConfigureAudit(
        this ModelBuilder model, string schema, string table);

    public static void ValidateAuditChanges(this DbContext database);
}
```

`AuditEntry` rejects empty IDs, null attribution, blank required strings, nonpositive detail
schema versions, null/undefined JSON and blank supplied optional strings. Accepted opaque
strings remain exact; no trimming or parsing. JSON is cloned before any tracking change.
OccurredAt is normalized to UTC; it is caller-selected observation time, not database commit
time or a total order. ID generation, clock selection and detail serialization stay explicit.
Outcome/reason/source/action/subject/schema meaning belongs entirely to the caller.
Anonymous attribution and a null tenant key are representable; consumer policy permits or
rejects them. A missing initiator is not inferred from actor, payload or message metadata.

`ConfigureAudit` supplies a model marker, ID primary key with no value generation, explicit
snake_case columns, required/nullability mapping and the default subject timeline index.
It returns the native builder. It supplies no schema/table defaults, query filter, provider
column type or retention rule.
Actor kinds persist the existing stable ActorKind values; null initiator kind/key means absent,
and Anonymous kind with null key remains distinct. No public row factory or mutable audit writer.

## Concrete consumer setup and usage

Native scoped DI; no extra registration helper or runtime context discovery:

```csharp
services.AddScoped<IAudit<SalesDbContext>, EfAudit<SalesDbContext>>();
services.AddScoped<IAudit<InventoryDbContext>, EfAudit<InventoryDbContext>>();

services.AddScoped<SalesAudit>();
services.AddScoped<InventoryAudit>();

// Each owning module's OnModelCreating, substituting its schema.
var audit = modelBuilder.ConfigurePostgresAudit("sales", "audit_entries");
audit.HasTenantOwnership(
    row => row.TenantKey!, () => RequiredOrganizationKey, "OrganizationScope");

// Both existing native sync/async save overrides, alongside their current guards:
this.ValidateAuditChanges();
```

Sales changes acquire the already established `IActorContextAccessor.Current`. The mutation
explicitly requires Human kind in consumer code, after its existing input/required-tenant
checks and before changing rows. Reading profiles remains independent of actor establishment.
Consumer registration supplies `TimeProvider.System` only if a clock has not already been
provided. The existing two saves and transaction remain; stage audit before the second save:

```csharp
// Existing ChangeAsync has begun its Sales transaction, loaded rows, advanced customer
// version and saved the customer. Authorization/admission happened through consumer ingress.
address.AddressLine = change.AddressLine;
audit.ProfileChanged(customer.Id, address.Id, change.ExpectedVersion, customer.Version);
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Sales deliberately excludes name/address text from details. The audit identifies the affected
profile/address and version transition; it is not a before/after content history. Validation,
missing profile, stale request, authorization/admission/antiforgery rejection and cancellation
produce no accepted-change audit. No denial-audit promise follows.

Inventory's inbox handler establishes a fresh ActorIdentity scope with a consumer-selected
System actor `inventory.stock-issue-worker` after validating receiving policy and tenancy.
`AddStockIssueInbox` registers the scoped identity holder/read/write aliases; ordinary command
registration does not acquire a new actor-context obligation. No initiator is currently known:
incoming correlation/causation are not human identities. No message-contract change is needed.

```csharp
// Inside StockIssueInboxHandler; the processor owns the already-active transaction.
var result = await commands.IssueAsync(request, cancellationToken);
if (result is StockPositionChangeResult.Changed accepted)
{
    audit.StockIssued(command.StockPositionId, accepted.Proposed.Version, command.Quantity);
    return;
}
// Existing explicit refusal reply path stays consumer-owned.
```

The existing Contract names the staged result `Proposed`; it is not committed success until
the processor commits. The accepted path's event/header/inline state,
audit and outgoing reply save before inbox completion and final commit. Refused and duplicate
deliveries create no accepted-change audit. Direct native Inventory commands, opening/receiving,
seeds, replay and rebuilding receive no automatic audit. Distinct deliveries still use current
expected-version/business rules; audit IDs are not semantic deduplication identities.

An independent executable consumer lives in the new `AuditPostgresTests` project: an ordinary
typed DbContext with a business row and custom-schema audit table, without Wholesale, Tenancy,
Persistence, Events or Messaging references. It explicitly stages Human/System attribution,
distinct optional initiator and tenantless work with native begin/save/commit. This proves
adoption outside the module layout without introducing a third business sample or template.

## Transaction protocol, errors and limits

1. The consumer creates/disposes operation scope and typed context, establishes/trusts required
   contexts, authorizes the operation and begins its native EF transaction. Inbox processing
   already owns that transaction; its handler neither nests nor replaces it.
2. `EfAudit` requires the configured model. `Stage` requires `Database.CurrentTransaction`,
   creates one row and binds its immutable envelope to that context and exact transaction.
   No I/O, save, commit, retry, publish, ambient transaction or transaction creation occurs.
3. Both native save overrides explicitly call `ValidateAuditChanges`. Added rows must match
   their staged envelope and original still-current transaction; changed/deleted tracked audit
   rows fail. Directly added unstaged records, envelope tampering and transaction replacement
   fail before SQL. Misconfigured model/lifecycle use throws InvalidOperationException;
   invalid envelope arguments use native argument exceptions. EF/provider failures propagate.
4. Caller saves explicitly and commits/rolls back/disposes explicitly. Multiple saves inside
   that transaction are supported. Cancellation of database work follows native EF; failed or
   canceled operations roll back and require a fresh operation context. A commit-response fault
   can be ambiguous and is not proof that no audit/business effect committed.
5. An audit's ID is unique within its table. Duplicate IDs fail through tracking or native
   uniqueness; Stage is not an idempotent operation. DbContext/staging are sequential, with no
   concurrent context use, nested audit transaction or cross-context transaction framework.

Native atomicity applies when the selected business effects and staged audit use the same
owning transaction. The guard validates staged audit participation; it cannot enforce that
every business mutation calls Stage, certify detail truth, grant authority, or prevent bypass
SQL/bulk mutation. Consumers own database grants, privileged maintenance, retention, indexes
and audit-query visibility. There is no audit read API, exporter, hash chain, tamper-evidence,
retention job, mandatory auditing middleware or SaveChanges interceptor. Other providers,
cross-module commits, denial auditing and initiator propagation remain deferred.

## Exact implementation scope after approval

| Files | Authorized purpose upon approval |
| --- | --- |
| New `src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/{Gloaming.Auditing.EntityFrameworkCore.csproj,AuditEntry.cs,IAudit.cs,EfAudit.cs,AuditRecord.cs,AuditModelExtensions.cs,AuditStageRegistry.cs}` | Exactly the surface/protocol above; registry is internal. No unused core package. |
| New `src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore.Postgres/{Gloaming.Auditing.EntityFrameworkCore.Postgres.csproj,PostgresAuditModelExtensions.cs,README.md}` | Owner-requested JSONB mapping composition. Generic EF package keeps no provider dependency; schema/table and returned native builder remain explicit. |
| New family/project READMEs and `src/Gloaming.Auditing/docs/transactional-audit.md` | Self-contained setup, guarantees, ownership and deferred limits. |
| New `docs/reports/e9-audit-context-research.md` | Cited primary-source findings about native context, JSON/EF equality and audit guidance. Research adds no executable guarantee or compliance certification. |
| New `src/Gloaming.Auditing/tests/AuditPostgresTests/{AuditPostgresTests.csproj,AuditConsumer.cs,EnvelopeTests.cs,TransactionTests.cs,GuardTests.cs}` | Independent ordinary EF consumer and focused envelope/native transaction/guard proofs. Reuse linked existing PostgreSqlFixture. |
| `samples/Wholesale/modules/Sales/Sales/{Sales.csproj,CustomerProfiles.cs,SalesAudit.cs,SalesRegistration.cs,SalesDbContext.cs}` | Explicit profile audit, consumer Human requirement/clock, typed DI, native mapping/save guard. No Contracts change. |
| Sales `Migrations/*_AddTransactionalAudit{.cs,.Designer.cs}` and `SalesDbContextModelSnapshot.cs` | New consumer-owned audit table only; preserve existing migrations/data. |
| `samples/Wholesale/modules/Inventory/Inventory/{Inventory.csproj,InventoryRegistration.cs,InventoryDbContext.cs,Messaging/StockIssueInboxHandler.cs,InventoryAudit.cs}` | Explicit accepted inbox audit, trusted per-operation worker identity, native mapping/save guard. Keep StockPositionCommands and wire Contracts unchanged; StockIssueMessages supplies separate direct-operation conversation IDs. |
| Inventory `Migrations/*_AddTransactionalAudit{.cs,.Designer.cs}` and `InventoryDbContextModelSnapshot.cs` | Module-owned audit table only; preserve existing inbox/outbox/event persistence identities. |
| New `samples/Wholesale/IntegrationTests/Api/ProfileAuditTests.cs`; supporting `PersistenceTests.cs` and `ProfilePersistenceTests.cs` | Attribution/tenant/details, consumer human-actor requirement, successful inclusion, omission oracle, faults and rollback. Existing migration proofs now compare all declared Sales migrations rather than assuming exactly one; existing profile regression coverage remains. |
| `samples/Wholesale/IntegrationTests/EventPersistence/{InboxDispatchTests.cs,OutboxDispatchTests.cs}` | Accepted inclusion/refusal/duplicate exclusion, audit/reply/completion faults, required-audit commit oracle and fresh retry. Extend existing PostgreSQL/RabbitMQ proofs, business subject/version/quantity matching without audit operation identifiers. |
| `samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/InboxJourney.cs`; HTTP/event demo READMEs | Show/query the committed audit in existing executable journeys and document actor/clock/setup obligations. No new worker or transport. |
| `ModulithFoundry.slnx`, `tests/ArchitectureTests/{ArchitectureTests.csproj,AdoptionDependencyTests.cs,AssemblyDependencyTests.cs}`, `.github/workflows/ci.yml`, `lefthook.yml` | Include the implemented family, check intended dependencies/standalone adoption, run focused audit proofs in existing CI/hook conventions. No new package versions or repository scripts. |
| This plan, `docs/plans/library-extraction.md`, `docs/design.md`; new `docs/adr/0011-explicit-transactional-audit.md` and `docs/reports/e9-explicit-transactional-audit.md` | Record the approved architecture, implemented scope, actual executions, evidence and remaining gaps. |

No archive, T1 template, Access, Purchasing, existing schema/route identities, generic workflow
utility, authentication adapter or messaging-library implementation changes are included.
Any broader need discovered during implementation returns for scope review.

## Required verification and review handoff

- Envelope validation, exact opaque keys, owned JSON after source-document disposal,
  Human/System/Anonymous distinction, optional initiator and UTC observation time.
- Independent PostgreSQL consumer: stage does not save; uncommitted audit/business rows are
  invisible from a fresh connection; explicit commit persists both; rollback/disposal persists
  neither. Multiple explicit saves, cancellation after an earlier save and fresh-context retry.
- Guard/model failures: missing model/transaction, swapped/ended transaction, unstaged or
  tampered records, tracked edits/deletes, native duplicate ID, sync and async save paths,
  typed-context isolation. No unsupported context reuse after rollback is advertised.
- Sales HTTP: accepted edit retains the trusted application Human actor and selected tenant,
  exact subject/version metadata and no sensitive text; conflicts/refusals/cancellation leave
  no accepted audit. Audit-write failure after the first customer save rolls back both business
  rows; dropping the fault permits a fresh request. A test-only deferred constraint rejects
  commit without the matching audit for the newly advanced version.
- Inventory: accepted issue commits stock version/quantity, retained facts, inline state,
  correctly attributed audit, reply and inbox completion together. Fault each participant
  (including audit and completion after save); verify all rollback and fresh retry produces
  exactly one accepted audit. Duplicate delivery and refused decisions add none. Assert
  business payload and tenant isolation; do not infer a human initiator.
- Native migrations update existing module databases and match runtime models; build,
  CSharpier, native style/analyzers, architecture dependency checks and relevant consumer suites.
  Active archive checksum verification may be run, without claiming archived tests as E9 proof.

### Review-checkpoint verification — 2026-10-09

New runs against the existing implementation, with no E9 C# changes:

| Existing consumer proof | Result |
| --- | --- |
| `HttpIdentityDemo.Tests`, method filter `*AddressFailureAfterCustomerSave*` | 1 passed, 0 failed, 0 skipped. Confirms rollback after the first Sales save and fresh-request recovery. |
| `EventPersistenceDemo.Tests`, class filter `*InboxDispatchTests`, method filter `*FailureAfterAcceptedDecision*` | 2 passed, 0 failed, 0 skipped. Confirms reply/completion fault rollback and fresh processing recovery. |
| Proposal's relative Markdown links | All 8 resolve to existing evidence files. |
| `git diff --check`; index inspection | Passed; index empty, documentation changes unstaged. |

Tests used `dotnet test --project <project> --no-restore` with the filters above and existing
native output flags. Initial sandboxed attempts failed during MSBuild discovery; reruns with
approved access to native build IPC/local containers passed. These results exercise the merged
consumer protocols, not an audit interface, audit migration or new audit guarantee. Archived
test results were inspected as historical evidence and were not rerun.

At this review checkpoint, repository/code inspection and these baseline reruns are evidence.
No new reusable mechanism has been proven. Library extraction is proposed;
sample classification/authorization/detail disclosure/retention remain consumer-owned. No new
template pattern or materialized composition is proven. The implementation report must replace
proposed guarantees with actual results and name any rejected or still-unproven obligations.
