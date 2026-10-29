# Native EF checkpoints

`EfCheckpointStore<TContext>` implements MAF 1.26.0 `ICheckpointStore<JsonElement>`. It depends
on MAF Workflows and EF Relational 10.0.13, with no vendor or Npgsql integration. MAF owns
checkpoint serialization/restoration; this store retains its opaque JSON without rewriting it.

## Explicit module composition

```csharp
modelBuilder.ConfigureAgentCheckpoints("documents");
// Both native save paths:
this.ValidateAgentCheckpointsChanges(admittedPartitionKey);

var store = new EfCheckpointStore<DocumentsDbContext>(
    db, partitionKey, conversationKey, operationCancellation);
```

The module owns the context, schema/table, migrations, admission and native runtime grants.
The returned entity builder supports consumer-owned filters. Partition/conversation keys are
trusted opaque strings up to 200 characters. The guard rejects tracked retained-row edits/
deletions and foreign-partition inserts; it does not guard arbitrary SQL or forged caller keys.

## Ordering, writes and recovery

Writes require an existing caller transaction. They flush all tracked changes through its
context, never independently commit, and must be serialized per conversation by the consumer.
The store allocates session-local sequence numbers from retained rows; the unique sequence
index rejects collisions but is not a replacement for serialization. The
[consumer](../samples/AgentPersistenceDemo/README.md) demonstrates an explicit PostgreSQL
transaction advisory lock. There is no library-owned lock, automatic retry or FIFO guarantee.

`RetrieveIndexAsync` returns oldest-first append order, optionally filtered by the selected
parent. Parent validation and retrieval include session and storage scope. Another partition,
conversation or session cannot retrieve the record. Missing/mismatched checkpoints fail.
Native index order does not select the application's authoritative workflow head or branch.

Payloads are stored as provider text, preserving nested native JSON metadata/property order.
Returned JSON owns its lifetime independently of the temporary document. Do not map these
opaque payloads to PostgreSQL `jsonb`; an outer out-of-order setting alone does not establish
nested MAF reader compatibility. PostgreSQL 18.6 is verified; other providers remain unproven.

The native store interface has no cancellation parameter. Constructor cancellation belongs
to this one operation; create a fresh store/context for each attempt. After rollback or uncertain
commit, abandon advanced native state and reload committed state before deciding another turn.
Repeated creates append different checkpoint IDs. No deduplication, history pruning, durable
background recovery or exactly-once model/tool effects are claimed.

See the [family proof map](../README.md#composition-and-proof).
