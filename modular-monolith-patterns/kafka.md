# Native Kafka delivery with PostgreSQL inbox/outbox

Compose Kafka through the existing `IMessagePublisher` and typed PostgreSQL inbox/outbox
interfaces. The application owns the native Confluent client, routing, wire contract,
trusted producer mapping, consumer group, bootstrap position and poison-record policy.
This recipe adds no Gloaming package, runtime or reusable library mechanism.

The executable [package-only consumer](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/Kafka/README.md) demonstrates
one sequential intake loop with Confluent.Kafka 2.16.0, Kafka 4.3.1 and PostgreSQL 18.6.
Its record format, table names and sample effects are consumer-owned examples.

## Publish retained intent

Enqueue `OutgoingMessage` in the producer module's ordinary native transaction. Save business
state and outbox work, then commit. Dispatch runs independently and claims work before invoking
the publisher; no producer database transaction spans Kafka I/O. See the
[PostgreSQL messaging contract](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/README.md).

The host owns a native `IProducer<byte[], byte[]>` configured with `Acks.All` and
`EnableIdempotence = true`. Supply it to the consumer's
[KafkaPublisher](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/Kafka/KafkaAdapter.cs) and reuse it for its selected
lifetime. Keep credentials/TLS, native timeouts and disposal in host composition.
`PublishAsync` validates the selected route, serializes the retained envelope and awaits
`ProduceAsync`. It returns normally only when the report says `PersistenceStatus.Persisted`.
Failed or unconfirmed acceptance throws. Cancellation does not prove broker rejection.

Keep MessageId unchanged across dispatch attempts. A producer session's idempotence does not
deduplicate a fresh producer after process loss. Broker acceptance can precede recorded outbox
completion; receiving intake must still deduplicate. The example's Kafka key is an item key;
it is neither delivery identity nor an authorization credential. Kafka partition order does not
establish domain causality or FIFO from an independently scheduled outbox.

## Commit intake before advancing a position

Select a native consumer group and bootstrap/reset policy explicitly. The fixture uses classic
groups, Range assignment and earliest reset for its new isolated topics; that is not a production
retention/replay decision. Disable **both** `EnableAutoCommit` and `EnableAutoOffsetStore`.
Native prefetch may continue internally, but the application accepts one delivered record at a
time. See the native client's [offset-management guidance](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html).

For each record:

1. Decode the selected wire contract and map its trusted native route to the application's
   stable producer namespace. MessageId, message alias/version and business metadata come from
   the validated envelope. An asserted producer name or TenantKey alone grants no authority.
2. Create a fresh scope and receiving DbContext. Begin an explicit native ReadCommitted
   transaction, call `IInbox<TContext>.ReceiveAsync`, then commit.
3. After committed intake, call `consumer.Commit(record)`. Both `Queued` and `AlreadyReceived`
   permit this acknowledgement. An unobserved/failed database commit does not.
4. Dispose the operation scope before accepting more work. Local business processing runs
   separately through `IInboxProcessor<TContext>` with its own fresh scopes and atomic local
   effects/completion. Do not hold Kafka intake across slow external work.

The [native adapter](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/Kafka/KafkaAdapter.cs) contains the complete
receive/commit composition. Both native EF save forms call the selected guards in the
[consumer mappings](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/Kafka/Database.cs). Production schema placement,
migrations, grants, tenant admission and reader permissions stay with the application.

If intake fails, stop that consumer attempt or deliberately seek/retry the same record before
reading further. Do not catch a failure, continue and later commit a higher position in that
partition. Kafka commits a next position; it does not acknowledge arbitrary independent records.
This recipe supplies no parallel-intake watermark, batch acknowledgement or partition scheduler.

Intake commit followed by offset-commit failure can redeliver. Reconcile through retained intake
in a fresh scope; do not repeat business work directly from the broker record. Ownership-loss
errors such as Local_State or UnknownMemberId do not roll back the committed receipt. They must
not become a blanket policy that ignores every Kafka exception. The host owns fail-stop/restart,
backoff and readiness; the proof observes an actual live group handoff and rejected stale commit.

## Reject or retain poison work deliberately

Malformed or conflicting records have not entered accepted intake. The library's
[inbox dead letters](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Messaging/docs/inbox-recovery.md) instead describe failures
after accepted intake; they cannot dispose an undecodable Kafka record.

The executable example first proves fail-stop at a conflicting offset while later valid work
remains unacknowledged. It then selects a different explicit policy: commit a receiver-owned
quarantine record before committing that position. Its controlled reasons distinguish invalid
envelopes and identity/content conflicts. Database/storage failures still propagate.

Quarantine retains a source-incarnation key, topic, partition, position, raw key/value bytes,
ordered headers and broker timestamp. Repeating disposition checks retained content rather than
overwriting it. Use a new admitted source incarnation after relevant cluster/topic recreation;
topic name plus numeric offset is not permanent source identity. The fixture uses unique topics
and never recreates one under its existing incarnation.

This is example policy and schema, not a Gloaming quarantine store. Applications choose operator
authority, disclosure, retention and repair/replay. Retained payloads/headers may contain sensitive
data. No replay UI, automatic discard, automatic evidence expiry or application admission follows
from this recipe.

## Lifetime and recovery

Bound native publication and operation budgets. Cancel a native `Consume` call cooperatively,
await it, then close/dispose its consumer; do not use that consumer concurrently from another
thread during close. Stop new work before shutdown. A pending database/offset boundary remains
uncertain under process loss; fresh operation scopes and retained delivery identity own recovery.
Producer delivery timers, native session limits and external process termination are separate
policies. No wrapper here bounds arbitrary user code or supplies a supervisor.

The host also owns source retention and receipt evidence lifetime. Deleting a processed receipt
ends its deduplication/content evidence; replay may execute old work again. A new group cannot
recover broker records removed by retention. Separate query projection rebuilds from historical
email, inference and other external effects. Kafka producer transactions do not atomically commit
PostgreSQL effects.

## Verification and limits

From the Gloaming repository root, with the pinned SDK/Node and a local Docker or Podman CLI:

```sh
npm ci --prefix tools/release --ignore-scripts
npm --prefix tools/release run verify:kafka
# Select the local engine explicitly when needed:
GLOAMING_CONTAINER_RUNTIME=podman npm --prefix tools/release run verify:kafka
```

The [runner](https://github.com/oxface/gloaming/blob/main/tools/release/verify-kafka.ts) packs current libraries, restores an external
consumer through source mapping into a fresh cache, verifies its exact four-package Gloaming
closure and zero ProjectReferences, then provisions its own isolated broker and PostgreSQL.
It retains source/archive hashes, redacted logs, per-scenario outcomes and cleanup evidence.
The [package workflow](https://github.com/oxface/gloaming/blob/main/.github/workflows/packages.yml) owns this native transport profile;
the ordinary seventeen-profile package command remains PostgreSQL-only.

The selected proofs cover process death before/after intake commit, accepted publication before
outbox completion, duplicate effects, native local rollback, conflicting/malformed disposition,
live max-poll ownership loss, dropped native Produce acknowledgements, broker restart, cancelled
consume/close and an owned literal v1 record. The database uncertainty case throws through a
native post-commit observer; it proves an unobserved committed result, not a TCP-level PostgreSQL
response fault. The frame proxy and process barriers are test-owned and absent from the recipe
adapter.

The tested topology is Linux x64, one combined broker/controller, one partition per topic, classic
groups and local plaintext. Replication-factor-one acceptance is no broker-availability claim.
Managed-service TLS/ACLs, cooperative/new consumer protocols, multiple partitions, parallel intake,
throughput, real production migrations and cross-released-client compatibility require their own
selected evidence. The initial literal v1 fixture does not certify two released library versions.
No Share Groups dependency or new Gloaming root runtime is introduced.
