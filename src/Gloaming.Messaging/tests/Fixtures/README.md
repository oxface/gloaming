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

Schemas, migration IDs and wire data are preserved. The Exports/Rendering CLR namespaces
and sample assemblies were renamed during consolidation; there are no external consumers.
The shared Linux child-process harness lives in [repository test support](../../../../tests/Support/ChildHost.cs).
