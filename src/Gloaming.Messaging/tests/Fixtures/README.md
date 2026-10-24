# Messaging executable test fixtures

These hosts preserve consumer policy and fault/compatibility evidence. The supported
public example is [durable delivery](../../samples/Delivery/README.md). No fixture is a
Gloaming package or a required application architecture.

| Fixture | Retained purpose | Owning tests |
| --- | --- | --- |
| [OutboxHost](OutboxHost/README.md) | HTTP publisher, authorized fixed-route retry, backlog/readiness, metadata-only maintenance and abrupt death/accounting failure. | [Exports](../DeliveryTests/Exports), [processes](../DeliveryTests/Processes/OutboxRecoveryProcessTests.cs). |
| [InboxHost](InboxHost/README.md) | HTTP intake, authorized receipt-generation retry and separately credentialed cleanup. | [Rendering](../DeliveryTests/Rendering), [processes](../DeliveryTests/Processes/InboxRetentionProcessTests.cs). |
| [BrokerJourney](BrokerJourney/README.md) | Finite real-broker round trip and mixed-schema executable proof. Reuses the actual worker's transport adapters. | [Broker tests](../DeliveryTests/Rendering/BrokerTests.cs), [schema processes](../DeliveryTests/Processes/SchemaCompatibilityProcessTests.cs). |
| [WorkflowDeployment](WorkflowDeployment/README.md) | Separately compiled Earlier/Current executables and independently authored literal v1 JSON. | [Deployment tests](../WorkflowDeploymentTests/DeploymentTests.cs). |
| [Recurrence](Recurrence/README.md) | Bounded calendar planning, actual TZDIR replacement and process recovery; independent maintenance/indication adapters. | [Provider/process tests](../OutboxPostgresTests/RecurrenceTests.cs). |
| [EntityWork](EntityWork/README.md) | Native-only transaction advisory locking, full-key namespacing and separate cooperating worker processes; no new reusable library mechanism. | [Provider/process tests](../OutboxPostgresTests/EntitySerializationTests.cs). |
| [Contributions](Contributions/README.md) | Independent all-chunk imports and two-of-three offers with consumer budget acceptance, retained technical completion/timeout and process recovery. | [Contract tests](../OutboxPostgresTests/ContributionTests.cs), [consumer/process tests](../OutboxPostgresTests/ContributionConsumerTests.cs). |
| [Wakeups](Wakeups/README.md) | Dedicated native listener, independent retained hints, typed inbox/outbox workers, reconnect/fallback and terminal fault supervision; no new reusable mechanism. | [Native composition tests](../OutboxPostgresTests/WakeupTests.cs). |
| [DueTimeIntent](DueTimeIntent/README.md) | Public finite occurrence operations with distinct consumer policies, atomic outbox release, stale-generation rejection and compiled recovery/readers. | [Provider/composition](../OutboxPostgresTests/DueTimeIntentTests.cs), [races](../OutboxPostgresTests/DueTimeIntentTests.Races.cs), [processes](../OutboxPostgresTests/DueTimeIntentTests.Deployments.cs), [library contract](../OutboxPostgresTests/DueIntentContractTests.cs). |

Schemas, migration IDs and wire data are preserved. The Exports/Rendering CLR namespaces
and sample assemblies were renamed during consolidation; there are no external consumers.
The shared Linux child-process harness lives in [repository test support](../../../../tests/Support/ChildHost.cs).
