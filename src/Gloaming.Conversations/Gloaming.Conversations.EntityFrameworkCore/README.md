# Ordered visible entries

`EfConversationTranscript<TContext>` retains append-only envelopes in the consumer's native
EF context. This .NET 10/EF Relational 10.0.13 package has no agent framework or model vendor.

```csharp
modelBuilder.ConfigureConversationTranscript("documents");
// Both native save paths:
this.ValidateConversationTranscriptChanges(admittedPartitionKey);

var transcript = new EfConversationTranscript<DocumentsDbContext>(db, partition, conversation);
// Existing caller transaction and serialized conversation write already established.
var entry = await transcript.AppendAsync(entryId, "document.proposed", 2,
    JsonSerializer.SerializeToElement(typedProposal, consumerJson), occurredAt, token);
var page = await transcript.ReadAsync(afterSequence: 0, maxCount: 100, cancellationToken: token);
```

The caller supplies a nonempty stable entry ID, nonblank type up to 100 characters, positive
schema version, defined JSON payload and occurrence time. Serialization/projection use native
consumer adapters; the library imposes no DTO family, renderer, action/version whitelist or
localization. Payload text preserves the supplied JSON. The module owns disclosure, authorship,
accepted-business audit selection, schema/migrations and SELECT/INSERT-only runtime grants.
Model/tool context is not automatically copied to visible entries.

Reads are oldest-first by sequence, after an exclusive nonnegative cursor, bounded to 1–1000
entries. Occurrence time does not determine append order and may differ from commit time.
Scope keys are trusted opaque strings up to 200 characters; they grant no authorization.
Consumer filters can be composed through the returned native entity builder. PostgreSQL 18.6
is verified; identity comparison under other providers/collations needs its own proof.

Writes require the caller's existing transaction and serialize per conversation. Sequence
allocation uses retained max+1 plus a unique database index; the index does not itself serialize
writers. Appends flush all tracked changes without commit. Duplicate entry IDs fail with native
tracking/database key errors; they are not treated as successful idempotent writes. Consumers
own logical-command deduplication/reconciliation when required. After failure, discard failed
state and recover from committed rows. No automatic retry is performed.

Tracked-save validation rejects retained modifications/deletions and foreign-partition inserts.
Raw SQL, bulk writes, privileged writers and schema changes require database permissions and
consumer controls. There is no retention/pruning or latest-message/model-context API. Reducing
working history does not alter this timeline; inspect the
[independent native agent composition](../../Gloaming.Agents/samples/AgentPersistenceDemo/README.md).
