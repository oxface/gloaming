# 0030: Native Kafka adapters preserve existing delivery authority

Date: 2026-10-25. Status: implemented as application guidance.

## Decision

Provide an executable native Kafka application recipe over the existing IMessagePublisher and
typed PostgreSQL inbox/outbox interfaces. Native producer/consumer ownership stays with the
application. No Kafka runtime, serialization standard, worker registry or new Gloaming package
is introduced. Routing, trusted origin, group/bootstrap position, quarantine and replay/retention
remain consumer policy.

Sequential intake commits its receiving transaction before committing the consumed record's
Kafka position. Unaccepted earlier records cannot be skipped by later acknowledgements. Committed
receipts reconcile duplicates after uncertain database/offset outcomes; local inbox processing
retains its independent atomic effects/completion protocol. Producer idempotence does not remove
the need for receiving deduplication after fresh-process publication.

## Consequences

This establishes **no new reusable mechanism**. The existing transport seam has native HTTP,
RabbitMQ and now Kafka example adapters. A wrapper over the Kafka client would leave the selected
application obligations exposed; no additional interface is justified by this recipe.

The [public guide](../../modular-monolith-patterns/kafka.md) and
[independently packaged native consumer](../../tools/release/consumers/Kafka/README.md) own the
complete selected composition and proofs. CI invokes the package-only provider/process profile;
the normal native project graph and PostgreSQL-only package command remain independently usable.

The test topology is deliberately one Linux broker/controller, classic groups and one partition
per topic. It does not select production HA, credentials/TLS, a managed vendor, arbitrary
partition concurrency, automatic poison policy or a shared replay engine. Parallel intake and
another reusable module require demonstrated coordination/depth and separate review.
