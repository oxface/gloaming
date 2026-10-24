# Technical contribution sets

The PostgreSQL messaging package supplies an explicit bounded named-contribution protocol.
It retains immutable expected identities, a consumer-selected required distinct count and original
completion/timeout envelopes. The selected count is technical readiness; participants, quorum
meaning, authorization, business approval and current-generation relevance remain consumer-owned.
No workflow graph, worker, clock, transaction, schema migration, save or commit is installed.

## Setup and native ownership

Import `Gloaming.Messaging.EntityFrameworkCore.Postgres`. Configure the three independent
capabilities in the owning context:

```csharp
modelBuilder.ConfigurePostgresOutbox("work", "outbox");
modelBuilder.ConfigurePostgresDueIntents("work");
modelBuilder.ConfigurePostgresContributions("work");

services.AddPostgresContributions<WorkDatabase>();
options.ConfigureGloamingCapabilities<WorkDatabase>(serviceProvider);
```

Shared save composition comes from `Gloaming.EntityFrameworkCore`. Alternatively, manually call
`ValidateContributionChanges()` before native synchronous saves and await
`ValidateContributionChangesAsync(token)` before asynchronous saves. Both include finite/outbox
guards. Cover both native `SaveChanges(bool)` overload paths. The constructor inspects the native
model without opening a connection. Missing/changed mappings fail before operation I/O.

Consumer migrations deploy `contribution_sets` and `contributions` before writers start. Schema
and table names are configurable quoted identifiers of at most63 UTF-8 bytes; column names/types
and complete keys are fixed. Set identity is ordinal `(ScopeKey, WorkId, GenerationId)`. Content
adds ordinal ContributorId to that key and has a restrictive complete-key foreign key to its set.
Scope/contributor IDs are nonblank, at most256 .NET characters and cannot rely on case folding.
Contributor IDs cannot contain NUL. Scope is independent of optional envelope TenantKey.

Expected identities/count and full retained terminal templates are versioned JSONB represented
as strings by the provided record. Native registration validates their JSONB representation before
contributions accumulate. A positive bigint Revision is the set concurrency token; State uses
explicit integer values1..3. Content is immutable name/positive schema/JSONB payload. Preserve
unfiltered independent tables, ordinal collation, full keys, native revision/precision and no
conversions/generated properties. DueAt, when present, is UTC timestamp precision6. Required
guards reject direct edits/deletes; explicit maintenance/removal requires separate authority.

Use a fresh owning context and original explicit native PostgreSQL ReadCommitted transaction
for each decision. Use each complete set generation once in that context; business rows and other
generations may participate. The caller saves and commits all proposals. The model also requires
finite/outbox tables when a particular definition selects no timeout.

## Register and contribute

```csharp
var key = new ContributionSetKey(scope, workId, generationId);
var definition = new ContributionSetDefinition(
    expected: ["alpha", "beta", "gamma"],
    required: 2,
    completion: originalReadyNotice,
    timeout: null);

await using var transaction = await database.Database.BeginTransactionAsync(token);
new PostgresContributionSets<WorkDatabase>(database).Register(key, definition);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

Register performs no I/O. It freezes1..100 distinct expected identities and required count1..N.
Selecting N requires all selected identities. Other counts are explicitly chosen by consumers;
weighted votes, mandatory subgroups, permission/admission, participant discovery, approval and
consolidation are not inferred. Original full completion notice is retained, including schema,
JSON payload and diagnostics. Use globally unused delivery IDs within the module outbox lifetime.
A retained complete generation cannot be reused. A business action receipt is separate when an
uncertain registration must recover its original key/result.

A later fresh transaction calls `AddAsync(key, contributorId, new ContributionValue(name,
schemaVersion, payload), token)`, then saves/commits. The operation locks the generation until
commit, reads at most101 retained contributions, and admits only an explicit expected identity.
The contribution clones its non-null JSON; source document disposal cannot change it. Equal
identity/name/schema/JSON-value repeat returns Duplicate without another notice. Different complete
content throws `ContributionConflictException`, including after terminal closure. Original wire
content remains the comparison basis; business upcasting/normalization is a consumer decision.
Object order and numeric lexical formatting do not distinguish equal JSON values; array order does.

Producer payloads and terminal templates require unique object property names recursively, including
objects inside arrays and escaped-equivalent names. JSONB discards earlier duplicates; rejecting
that ambiguous shape before staging keeps equal retries and native saved-evidence checks coherent.
Other native JSONB limits apply at save time. Payload schemas remain opaque; the protocol does not
interpret fields or select a serializer/reader. Bound allowed payload sizes in the consumer.

A new identity below the selected count returns Accepted. Reaching it stages Completed plus the
original outbox notice in the same transaction. Already retained content remains readable. A new
unrecorded expected identity after closure returns AlreadyCompleted/AlreadyTimedOut and is not
retained. Equal retained duplicates still return Duplicate. These results are proposals until commit;
technical completion is not business success or external acceptance.

## Optional timeout and races

Select `ContributionTimeout(dueAtUtc, originalSignal, originalTimeoutOutcome)` explicitly in the
definition. Completion, signal and timeout-outcome delivery IDs must be distinct. Register stages
the original signal through finite intents with key `(ScopeKey, WorkId, GenerationId)`; this key
is reserved for that generation's timeout. Release/publishing/intake are separately selected roles.
The signal's receiver validates trusted origin and consumer relevance before invoking timeout.

`TimeoutAsync(key, observedAtUtc, token)` locks the same generation. Before retained due time it
returns NotDue. An eligible open set stages TimedOut plus its original outcome. Repeat closure
returns AlreadyTimedOut/AlreadyCompleted without another notice. A definition without timeout
rejects this operation. There is no ambient clock; use explicit UTC microsecond instants.

Completion or timeout also suppresses the pending finite signal in the same save/commit. An
already released signal cannot be recalled; its receiver must recheck terminal/current relevance.
Missing timeout evidence fails rather than claiming successful suppression. Cancellation of a
finite signal alone does not close or cancel its contribution set.

Last arrival and eligible timeout have one generation-lock winner. Arrival does not read a clock:
it can win after the nominal due instant if timeout has not closed work. This is not strict business
deadline enforcement, FIFO or fairness. Consumers enforce their own deadline/admission rules and
keep source-row/head lock acquisition order consistent. A due timer is an invitation to reconsider
current state. Registering another generation does not deactivate old business relevance; consumers
lock/update their own current-generation row and reject stale input/outcomes.

## Save, recovery and deployment

Required guards retain exact original/current proposal snapshots for set, contribution and outgoing
participants, plus finite suppression. Omitted/detached/mutated/prematurely accepted participants or
changed EF original values reject before SQL. Unchanged proposals require matching native stored
values in the original transaction, including after savepoint rollback. Successful true saves and
false saves followed by native AcceptAllChanges support repeated validation. Native failure,
cancellation, conflict or validation failure requires disposal and fresh recovery.

A lost commit response may already mean success. Fresh equal contribution/timeout attempts read
retained state and preserve original notice identity. Do not reuse the failed context or blindly
replay an old proposal. Outbox publication still has its documented acceptance uncertainty and
receivers need retained intake/business idempotency. No exactly-once external effect follows.

Runtime credentials need schema USAGE, set SELECT/INSERT/UPDATE, content SELECT/INSERT, finite
SELECT/INSERT/UPDATE and outbox SELECT/INSERT. No content UPDATE/DELETE, set DELETE, schema CREATE
or outbox completion authority is required. These are privileged module operations, not tenant
admission or an RLS claim. SELECT covers retained payloads/templates; authorize inspection separately.

Deploy consumer-owned tables/grants and compatible readers before selected writers. Retain set,
contribution and finite identity evidence for the module lifetime; removal ends duplicate/content/
generation protection and can permit old work again. No automatic pruning or expiry of evidence
is supplied. This is one initial provided format; later schema/protocol changes need owned literal
fixtures and actual old/new binary rollout proofs. Existing finite/outbox consumers remain independent.

The [native consumers](../tests/Fixtures/Contributions/README.md) demonstrate all-chunk import
readiness and a two-of-three offer indication whose budget policy can still reject technically
complete replies. Their business state and readers remain application-owned. Provider tests cover
native races, rollback, response loss, participant suppression, process recovery and retained JSON.
The independent [NuGet adopter](../../../tools/release/consumers/Contributions.cs) owns migrations,
restricted grants and initial retained-format evidence.
