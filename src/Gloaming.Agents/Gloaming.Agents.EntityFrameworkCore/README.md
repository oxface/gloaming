# Native EF working history

`EfChatHistoryProvider<TContext>` implements MAF 1.26.0 `ChatHistoryProvider` with immutable
full-message snapshots. It depends on MAF abstractions and EF Core Relational 10.0.13;
workflow execution, Npgsql and model-provider clients are not selected by this package.

## Explicit setup

```csharp
// The consuming module's OnModelCreating; schema/table stay module-owned.
modelBuilder.ConfigureAgentHistory("documents");
// Both native SaveChanges overrides:
this.ValidateAgentHistoryChanges(admittedPartitionKey);

var history = new EfChatHistoryProvider<DocumentsDbContext>(
    db, partitionKey, conversationKey, "editor", reducer: optionalNativeReducer);
// Supply history to native ChatClientAgentOptions.ChatHistoryProvider.
```

The mapping returns the native entity builder for consumer filters/configuration. Add the
module's own migrations and SELECT/INSERT-only runtime grants. Keys are trusted opaque strings
up to 200 characters, compared with the selected database's identity semantics; they grant no
permission. The guard rejects modifications/deletions of retained rows and foreign-partition
inserts in tracked saves. Raw SQL, bulk paths, privileged writers and altered mappings need
their own enforcement. PostgreSQL is verified; custom collations/other providers are unproven.

## Snapshots and native hooks

Native `ProviderSessionState<HistoryState>` records a session key and the exact selected snapshot
ID under a stable agent-specific state key. Restoration loads that version within partition,
conversation, agent and session scope. An unavailable version fails rather than loading latest
history or silently discarding context. A restored earlier session may append a new branch;
it cannot import later/abandoned messages. Different agent keys maintain separate provider state.

Messages use native MAF JSON options, preserving typed tool calls/results and order. Optional
serializer options are copied/frozen; mutable converters/resolvers remain caller responsibilities.
Store text without rewriting metadata order. MAF's base hooks filter already-loaded history
and skip failed invocations; this provider overrides only the native load/store methods.
An optional native `IChatReducer` reduces each successful snapshot, leaving independent visible
entries untouched. Snapshot copying has cumulative storage cost; no pruning is performed.

## Transaction and failure contract

Writes require the caller's existing native EF transaction and serialized conversation turns.
Every store call flushes **all** pending tracked changes through native `SaveChangesAsync`;
it does not independently commit. Consumers may deliberately make several saves in that
transaction. Scope the provider/context to one operation; never share a DbContext across runs.

After save/commit failure or rollback, discard the advanced session and failed context, then
reconstruct from the last committed checkpoint. The provider does not undo in-memory state,
retry an invocation or reconcile uncertain commits. Repeated successful invocations append
new snapshots; no invocation-id deduplication or external-effect guarantee is supplied.
Failed-input presentation and separately retained failure entries remain consumer policy.

See the [complete composition and proofs](../README.md#composition-and-proof).
