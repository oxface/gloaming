# Gloaming.CommandReceipts.EntityFrameworkCore.Postgres

Logical-command receipts for native EF/PostgreSQL applications. Equal retained attempts return
the original business result; different comparison input under the same logical identity
conflicts. The package owns transaction-scoped serialization, retained comparison, immutable
result staging and tracked-save guards. It has no Messaging, actor, tenancy, HTTP or Hosting
dependency. Dependencies are the native Npgsql EF provider and Gloaming.EntityFrameworkCore.

## Native setup and operation

Select one provided technical table in your context's `OnModelCreating`:

```csharp
model.ConfigureCommandReceipts("orders", "command_results");
```

Register the scoped context and validation explicitly:

```csharp
services.AddPostgresCommandReceipts<OrdersDatabase>();
services.AddDbContext<OrdersDatabase>((provider, options) =>
    options.UseNpgsql(connection)
        .ConfigureGloamingCapabilities<OrdersDatabase>(provider));
```

Native EF migrations, schema placement, connection/provider configuration and deployment belong
to the consumer. Explicit schema/table names must fit PostgreSQL's standard 63 UTF-8 byte
identifier limit; silent truncation must not alias differently locked names. No operation migrates,
provisions resources, starts workers or commits. As an
alternative to DI/interception, construct `PostgresCommandReceipts<OrdersDatabase>(database)` and
call `ValidateCommandReceiptChanges` from **both** native sync/async save overrides before base
saving. The [package-only adopter](../../../tools/release/consumers/CommandReceipts.cs) exercises
that alternative. Omitting the selected validation integration bypasses its tracked-write guards.

After authenticating/admitting the operation and checking current resource permission:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var attempt = await receipts.AcquireAsync(
    new CommandReceiptKey(admittedScope, "submit-order", logicalActionId),
    canonicalInputV1,
    token);
if (attempt.Result is { } retained)
    return ReadSupportedResult(retained); // never execute again because decoding fails

var result = await MakeBusinessDecision(token);
if (!result.Accepted)
    return result; // no receipt; dispose the transaction/scope

attempt.Retain(new CommandReceiptContent(1, EncodeResultOrReference(result)));
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
return result;
```

The caller executes business code. Acquire alone serializes attempts; completed-result protection
requires Retain plus saving/committing that receipt with the accepted changes. Retain stages
only a completed receipt, never a separately committed claim. The returned result is not proof
of a new commit. An equal replay returns the **original** outcome, possibly older than current
business state. Query current state separately when needed.

## Identity, concurrency and atomicity

The full key is `(ScopeKey, Operation, OperationId)` within the selected physical table.
Scope/operation are opaque, untrimmed, ordinal strings, bounded to 256/128 UTF-16 code units;
operation ID is a nonempty GUID. PostgreSQL key columns use deterministic `C` collation. Inputs
are nonnull text, compared ordinally. No JSON equivalence, normalization, hashing of business
payloads or serializer is chosen. PostgreSQL text cannot contain NUL; valid UTF-8 is required.
Choose stable comparison bytes, including explicit format meaning where necessary.

Use **one acquired command per fresh context scope**, an active native **ReadCommitted**
transaction, and that context's same transaction through save/commit. Ambient transactions,
shared concurrent context branches and context pooling with scoped interceptor options are
unsupported. Native shared-connection transactions remain caller-owned; separate databases
cannot share this atomic receipt. Other operation IDs compete through ordinary business
concurrency. The library does not infer equal intent from entity IDs or equal-looking payloads.

Acquire locks a hash of the physical schema/table and complete key. A competing same-key
writer waits for commit/rollback, then reads in a fresh ReadCommitted statement. Hash collisions
only serialize unrelated actions; full keys/comparison decide identity. All writers claiming
this protocol must cooperate. Stable placement, keys and locking must survive rolling deployments.

Failed acquisition faults that context's receipt operation. Save guards reject altered,
detached, directly added, edited/deleted or transaction-separated receipt proposals; one
attempt cannot retain twice or replace a retained result. Native sync/async saving and
`acceptAllChangesOnSuccess: false` are supported; use native AcceptAllChanges before another
save after a false-acceptance save. Guards do not detect every native save/commit failure.
On error, cancellation or uncertain commit, dispose the transaction and scope, then reconcile
from fresh state using the original logical ID. Never retry a failed tracked operation in place.

Cancellation of a waiting caller does not cancel the owner or prove that its work failed.
Connection/process death releases transaction locks; rollback leaves no completed receipt.
A lost commit response can recover the durable result in a new authorized scope. External
provider calls are outside that database atomicity; no exactly-once external effects are promised.
Native command timeouts and operational capacity remain consumer configuration.

## Retention and authorization contract

**Retain completed receipts for the consuming module's lifetime** as the initial supported
policy. There is no clock, TTL, automatic expiry, prune operation or cleanup worker. Storage
grows with accepted actions. References can minimize retained sensitive data, provided their
meaning remains retrievable by compatible authorized callers. The consumer chooses what to
retain under its own data/permission policy.

Tracked runtime receipts are append-only. Explicit native owner removal or table/module
retirement **ends duplicate protection** for removed keys. Old actions may execute again if
still admitted; deleting the business entity alone does not erase its receipt. Retire admission
to old action identities before removing evidence if re-execution is forbidden. The library
supplies no permanent registry outside this table and no safe pruning-with-continuing-protection
claim. A future expiry feature would require a separately reviewed contract and race proofs.

Scope is not admission. Authenticate, authorize and admit on every attempt before exposing
the retained result. The library explicitly queries the full key, but selects no tenant policy
or resource grants. Compose native EF filters/ownership/RLS as required, with policies that
allow the admitted operation to see its own receipt and reject foreign writes. The provided
record's scope column can be renamed natively; its fixed six scalar fields, full primary key,
single-table placement, positive format constraint and ordinal key equality remain required.
Use explicit restricted runtime rights, typically SELECT/INSERT; privileged SQL/bulk writes,
credential-holder policy bypasses and options omitting guards remain consumer responsibilities.

## Deployment and result readers

`CommandReceiptContent.FormatVersion` identifies the **consumer's** opaque result format.
The library does not decode it. Keep old readers and comparison encodings until their retained
actions are retired. An additive new request field can be excluded from a stable V1 comparison
only when the consumer decides it has no action meaning. Changed business meaning needs a new
action identity or explicit migration, rather than silent equality under an old key.

Deploy readers before new result writers. An older consumer encountering an unsupported result
must fail without making the business decision again. Restoring a compatible reader recovers
the same result. A changed operation name/scope/table would reopen identities; changing the lock
protocol can stop old/new writers coordinating. The library's initial storage/locking protocol is
V1; the deployment fixture proves old/new **consumer** binaries using this first implementation,
not compatibility with an earlier nonexistent library release.

The V1 advisory-lock frame is SHA-256 over UTF-8 prefix
`Gloaming.CommandReceipts/PostgreSQL/v1` followed by one NUL byte, then five strings in order:
schema, table, scope, operation, lowercase `D` GUID. Each string has a signed 32-bit big-endian
UTF-8 byte length followed by its bytes. Interpret the first eight digest bytes as a signed
big-endian Int64. Frozen vector: `deployment`, `logical_actions`, `admitted-tenant`, `publish`,
`1d700000-0000-0000-0000-000000000001` gives `-2187035804754743166`. Do not change this framing
in place in a later library deployment.

The required storage columns are scope key, operation, operation ID, comparison input, result
format version and result text. Native placement/column mapping and migrations belong to the
consumer. Compatible upgrades preserve those meanings and existing keys/rows. The result is a
business DTO/reference, not an HTTP status/header/body replay cache.

## Executable proofs

The [independent provider/deployment suite](../tests/CommandReceiptTests/CommandReceiptTests.csproj)
checks concurrency, save/rollback/uncertain commit, cancellation, append-only guards, scope/key
isolation and explicit removal ending protection. Separate Earlier/Current consumers exercise
request/result evolution, incompatible readers, overlapping processes, killed writers before/
after commit, and the frozen native lock vector. The [fixture guide](../tests/Fixtures/ReceiptDeployment/README.md)
records their exact scope. [Sales](../../../samples/Wholesale/modules/Sales/command-receipts.md)
adds actual permission checks, RLS, required reactions and workflow/outbox composition. These
proofs do not certify a provider matrix, arbitrary custom mappings or production capacity.
