# Native Kafka package consumer

This independently compiled consumer adopts the existing PostgreSQL inbox/outbox interfaces
with native Confluent.Kafka. It is test-owned application guidance, not Umbrine implementation
or a new library package. The [public recipe](../../../../modular-monolith-patterns/kafka.md)
describes the composition, policies and supported limits.

Run from the repository root:

```sh
npm ci --prefix tools/release --ignore-scripts
npm --prefix tools/release run verify:kafka
```

The TypeScript [runner](../../verify-kafka.ts) requires the pinned SDK/Node and a local Docker or
Podman CLI. Set GLOAMING_CONTAINER_RUNTIME to docker or podman to select it explicitly. It packs
current libraries, copies this consumer outside the checkout, generates exact PackageReferences
and source mapping, restores a fresh cache and builds with recommended analyzers/warnings as
errors. Only Messaging, its EF/PostgreSQL layers and Gloaming.EntityFrameworkCore are selected.
Native broker/database fixtures are never silently skipped.

The fixture separates the [native adapter](KafkaAdapter.cs), [consumer mappings](Database.cs),
[process journey](Journey.cs), [additional recovery cases](RecoveryCases.cs),
[native commit observation faults](CommitObservation.cs), [process lifetime](ProcessHost.cs)
and a [plaintext test frame proxy](BrokerProxy.cs). Faults do not add test hooks to the adapter.
The [owned v1 JSON](Fixtures/changed.v1.json) is literal reader evidence, not encoder output.
Its sample values and business-effect table are fixture policy.

The runner owns only two uniquely named containers, random local ports and per-run generated
database credentials. It restarts its own Kafka container when the journey requests that test
step. Ordinary success/failure removes its containers and anonymous volumes and preserves temporary evidence. It
does not stage files, change the Git index, modify Umbrine or publish packages.

The printed temporary directory contains archives, generated consumer/cache, execution.log,
results.json, manifest.json and cleanup.json. Native outcomes, source/archive hashes and package
closure are separate from resource cleanup status; command success requires both. The initial
test schema is generated for a new isolated database, not a production migration/rollout proof.
Broker positions and quarantine rows are independent from business-effect and delivery identity.
Source hashes describe the copied files used to build the consumer. Release tooling checks also
exercise denied/disconnected cleanup, confirmed absence and continuation after removal failures.

The proof uses one broker, one partition per topic, classic Range group assignment, local
plaintext and sequential explicit commits. It establishes no reusable mechanism, production
authorization, HA, parallel/batch intake, replay UX or throughput guarantee. The database fault
is a post-commit observation failure; the Kafka acknowledgement fault drops actual native
Produce response frames. See the public recipe for the complete evidence boundary.
