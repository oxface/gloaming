# Native Kafka package consumer

This independently compiled consumer adopts the existing PostgreSQL inbox/outbox interfaces
with native Confluent.Kafka. It is test-owned application guidance, not Umbrine implementation
or a new library package. The [public recipe](../../../../modular-monolith-patterns/kafka.md)
describes the composition, policies and supported limits.

Run from the repository root:

```sh
npm ci --prefix eng/release --ignore-scripts
npm --prefix eng/release run verify:kafka
```

The TypeScript [runner](../../verify-kafka.ts) requires the pinned SDK, Node and a
Docker-compatible socket. It packs current libraries, copies this consumer outside the
checkout, generates exact PackageReferences and source mapping, restores a fresh cache
and builds with recommended analyzers and warnings as errors. It selects Messaging, its
EF/PostgreSQL layers and Gloaming.EntityFrameworkCore. Both providers are required.

For rootless Podman on Linux, start its user socket and point Testcontainers at it.
Disable the resource reaper when required by the rootless engine.

```sh
systemctl --user start podman.socket
DOCKER_HOST="unix://${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/podman/podman.sock" \
  TESTCONTAINERS_RYUK_DISABLED=true npm --prefix eng/release run verify:kafka
```

The fixture separates the [native adapter](KafkaAdapter.cs), [consumer mappings](Database.cs),
[process journey](Journey.cs), [additional recovery cases](RecoveryCases.cs),
[native commit observation faults](CommitObservation.cs), [process lifetime](ProcessHost.cs)
and a [plaintext test frame proxy](BrokerProxy.cs). Faults do not add test hooks to the adapter.
The [owned v1 JSON](Fixtures/changed.v1.json) is literal reader evidence, not encoder output.
Its sample values and business-effect table are fixture policy.

The [fixture](ProviderFixture.cs) uses the repository's pinned .NET Testcontainers package.
It provisions two uniquely named containers with dynamic loopback ports and waits for
provider readiness. The consumer keeps its Kafka proxy listener bound from the
moment it chooses its port. When the journey requests a restart, Testcontainers restarts its
broker and waits again. The proxy uses the broker's current mapped port while its advertised
endpoint stays bound. Ordinary success or failure removes the owned containers and anonymous
volumes and preserves temporary evidence.

The printed temporary directory contains archives, generated consumer/cache, execution.log,
results.json, manifest.json and cleanup.json. Native outcomes, source/archive hashes and package
closure are separate from resource cleanup status; command success requires both. The initial
test schema is generated for a new isolated database, not a production migration/rollout proof.
Broker positions and quarantine rows are independent from business-effect and delivery identity.
Source hashes describe the copied files used to build the consumer. The
[native loopback suite](../../tests/KafkaProxy.Tests.csproj) covers the frame proxy's socket
faults independently of Kafka and PostgreSQL.

The proof uses one broker, one partition per topic, classic Range group assignment, local
plaintext and sequential explicit commits. It establishes no reusable mechanism, production
authorization, HA, parallel/batch intake, replay UX or throughput guarantee. The database fault
is a post-commit observation failure; the Kafka acknowledgement fault drops actual native
Produce response frames. See the public recipe for the complete evidence boundary.
