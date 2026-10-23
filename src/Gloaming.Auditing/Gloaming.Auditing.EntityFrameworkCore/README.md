# Gloaming.Auditing.EntityFrameworkCore

`IAudit<TDbContext>.Stage(AuditEntry)` captures an explicitly selected audit in the same
typed native EF context and active transaction as its business change. It performs no I/O,
save, commit, retry or publication. `EfAudit<TDbContext>` supplies the implementation.

Dependencies are `Gloaming.ActorIdentity`, `Gloaming.EntityFrameworkCore` and native EF Relational.
No runtime context/accessor, clock, tenant, codec, provider, host or bus is resolved by the
library. ActorContext is an explicitly supplied immutable value, not an authorization rule.

For [shared save composition](../../Gloaming.EntityFrameworkCore/README.md), ConfigureAudit declares
the audit model requirement. Register AddAuditValidation<TDbContext>() and install
ConfigureGloamingCapabilities<TDbContext>(provider) in native scoped options. It runs the existing
ValidateAuditChanges after preparation. Explicit validator overrides below remain a supported
alternative. Audit staging still requires its existing explicit transaction and actor values.

## Consumer setup

Register the concrete owning DbContext using your native provider and your native scoped DI:

```csharp
services.AddScoped<IAudit<AppDbContext>, EfAudit<AppDbContext>>();
```

Use `ConfigureAudit` in that context's `OnModelCreating`, with explicit schema/table choices.
The returned native builder is editable; provide provider JSON mapping explicitly.
For PostgreSQL:

```csharp
var audit = modelBuilder.ConfigureAudit("operations", "accepted_log");
audit.Property(row => row.Details).HasColumnType("jsonb"); // PostgreSQL consumer choice.
audit.Property(row => row.Id).HasColumnName("audit_id"); // Optional native customization.
```

ConfigureAudit includes the relational
index ix_audit_subject_timeline on TenantKey, SubjectType, SubjectKey, OccurredAt and Id.
No additional setup call is required. Consumers can rename or replace the index through
native EF model configuration. Ownership, query filters and additional indexes remain
consumer choices.

The helper maps one provided AuditRecord type per context: ID primary key without generated
values, explicit snake_case columns, required/optional fields and the subject timeline
index. It selects no query filter, database-time default, ownership policy or retention behavior.
Configure those yourself when needed. TenantKey is an optional exact opaque string;
the technical envelope does not interpret null as access to all tenants.

Explicitly call `this.ValidateAuditChanges()` before the base call in **both**
`SaveChanges(bool)` and `SaveChangesAsync(bool, CancellationToken)` overrides.
The guard validates tracked audit participation, not all business writes. Native EF bulk/raw
SQL bypasses it. Context construction, connection, migrations and final save/commit stay yours.

## Explicit usage

The caller chooses the successful business change, clock, ID and safe details before staging:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
db.Add(changedBusinessRow);

var now = timeProvider.GetUtcNow();
audit.Stage(new AuditEntry(
    id: Guid.CreateVersion7(now),
    occurredAt: now,
    attribution: operationActor, // ActorContext already established by the consumer.
    source: "documents",
    action: "document.changed",
    subjectType: "document",
    subjectKey: documentKey,
    outcome: "accepted",
    schemaVersion: 2,
    details: JsonSerializer.SerializeToElement(new { Version = 4 }),
    reasonCode: "customer-request"));

await db.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Inside a transactional inbox handler, use the processor's existing transaction and stage only;
its processor owns save/completion/commit. Do not nest a new transaction.

AuditEntry requires a nonempty ID, attribution, nonblank source/action/subject type/
outcome, positive detail schema version and non-null/non-undefined JSON. SubjectKey is the
optional item/aggregate ID; null permits a subject-wide or global event without inventing
an item ID. Supplied optional subject/tenant/reason strings cannot be
blank. Accepted strings are preserved
exactly, with no parsing, trimming or case conversion. TenantKey is the final optional
constructor parameter, after ReasonCode. ReasonCode is an optional consumer-selected business
explanation, distinct from Outcome. For example, an accepted stock adjustment could use
action stock.adjusted with reason physical-count or damaged-stock. These codes explain why
the stock changed and can be displayed or filtered without interpreting Details. Consumers
own their vocabulary; both sample accepted-change wrappers currently omit a reason.
Details are cloned, independent of the original JsonDocument. OccurredAt is normalized to UTC and means caller observation time,
not commit time or a total order. PostgreSQL timestamp storage has microsecond precision.

ActorContext supplies executing Actor kind/key and optional initiator. Human/System kinds
remain distinct even with equal keys. Explicit Anonymous kind and absent initiator are
different. The library permits representable anonymous/tenantless attribution; consumer
policy decides whether it is acceptable. It never invents an initiator, reads HttpContext,
or treats message metadata as authority.

## Contextual consumer wrappers and timelines

Avoid constructing the full envelope in each business handler. A scoped consumer wrapper
can own its module source, accepted classification, schema and safe payload, and capture
actor/tenant, clock and generated ID:

```csharp
// Sales, after its accepted versioned update; owning transaction already active.
audit.ProfileChanged(customer.Id, address.Id, change.ExpectedVersion, customer.Version);

// Inventory, after an accepted inbox decision; processor owns save/commit.
audit.StockIssued(command.StockPositionId, accepted.Proposed.Version, command.Quantity);
```

These are the sample's internal
[SalesAudit](../../../samples/Wholesale/modules/Sales/Sales/SalesAudit.cs) and
[InventoryAudit](../../../samples/Wholesale/modules/Inventory/Inventory/InventoryAudit.cs),
not additional library APIs. They obtain trusted attribution and tenancy from the established
accessors; neither depends on Activity or incoming message metadata. The library introduces
no ambient context or automatic metadata fallback. Correlation, causation and trace IDs are
omitted from audit until a concrete business use earns them. Diagnostic telemetry remains
host-owned OTel setup; existing messaging metadata stays in inbox/outbox envelopes.

Both modules receive the subject timeline index from ConfigureAudit, without a
separate indexing call. Consumers can query a tenant-visible item timeline using native EF:

```csharp
var timeline = await db.Set<AuditRecord>()
    .Where(row => row.SubjectType == "customer" && row.SubjectKey == customerKey)
    .OrderBy(row => row.OccurredAt).ThenBy(row => row.Id)
    .ToArrayAsync(cancellationToken);
```

This assumes the consumer's tenant query filter and read authorization. Global entries are
queried separately with SubjectKey == null. Time/ID supplies deterministic display ordering,
not database commit order; no timeline endpoint, authorization or query service is added.

Keep provided EF rows as classes with reference identity. The pending-envelope guard
compares exact GetRawText values, including formatting. Structural JSON value equality is a
different comparison contract.

Source/action/outcome/reason/schema and detail disclosure have no library taxonomy.
Audit IDs are unique per table and Stage is not idempotent. Delivery/business semantic
deduplication remains a separate consumer responsibility.

## Payload schema versions

SchemaVersion describes the JSON Details contract for a consumer's source/action. It is not
the database migration version or the item's business version. Keep the payload type and its
version together inside the consumer wrapper:

```csharp
private sealed record StockIssuedDetailsV1(long Version, decimal Quantity)
{
    internal const int SchemaVersion = 1;
}

// The wrapper supplies both; its business caller supplies neither a schema nor JSON.
var schema = StockIssuedDetailsV1.SchemaVersion;
var details = JsonSerializer.SerializeToElement(new StockIssuedDetailsV1(version, quantity));
```

SalesAudit and InventoryAudit use this pattern. For an incompatible shape or changed meaning,
introduce V2 and its matching version. Preserve the V1 definition for readers of historical
entries. A renderer can dispatch by source/action/SchemaVersion using consumer-owned native
JSON deserialization. Additive compatibility is also a consumer decision. The library cannot
infer a semantic breaking change from property names, CLR type names or a JSON hash. No
audit codec, upcasting registry or automatic payload rewrite is added by this capability.

## Staging evidence and lifetime

AuditStageRegistry attaches one Registration to each AuditRecord instance through a
ConditionalWeakTable. Its key is the row object, not its GUID or the DbContext. The value
contains the ContextId (including pool lease), original immutable AuditEntry and exact native
transaction wrapper. Find reads this in-memory evidence only when the requesting ContextId
matches; the save guard then compares the active transaction by reference and checks the
envelope. A copied/materialized/unstaged row has no evidence; detach/reattach to another
context or lease does not establish it.

The table does not keep the row alive. While a caller or tracker holds the row, the value
retains the transaction wrapper and may retain related managed objects. It neither opens
nor disposes a transaction; commit/rollback/disposal stay native caller obligations. Once
external row references are gone, the row/evidence graph can be garbage-collected. This is
not eviction at commit and not transaction reuse. Already-saved unchanged rows are skipped;
a failed/rolled-back operation still needs the documented fresh-context recovery.

OutboxEnqueueRegistry uses the same row/context/transaction association. The event-sourcing
maintenance registry instead attaches evidence to a DbContext instance for its exact native
replacement. These are object-lifetime associations, unlike event contract registries keyed
by durable name/version, which must remain strongly held for decoding historical payloads.

## Errors and supported contract

Missing model configuration, missing/ended native transaction, changed transaction, unstaged
record insertion, staged-envelope changes and tracked audit updates/deletes fail with
InvalidOperationException. Invalid envelope inputs use native argument exceptions.
EF/provider/concurrency failures propagate; no business fault classification is installed.

Multiple explicit saves are supported in the original transaction. Cancellation/failure
requires rollback/disposal and a fresh context for recovery. Native commit-response loss can
be ambiguous. An unchanged already-saved tracked record is not evidence that a rolled-back
operation can safely reuse its context.

PostgreSQL proofs cover no implicit save, cross-connection visibility, explicit commit,
rollback, participant faults, cancellation, fresh recovery, context/transaction association,
duplicate IDs and guarded tracked writes. See the [family contract](../docs/transactional-audit.md).

The guard cannot detect a business change that omitted Stage, prove detail truth, prevent
privileged SQL, grant permission or make records tamper-evident. Consumer-specific commit
oracles prove the two selected workflows include their matching audit. Denial durability,
cross-module commits, retention jobs, exporters, query services and other providers are deferred.
