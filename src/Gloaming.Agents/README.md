# Native agent persistence

Optional .NET 10 integrations with Microsoft Agent Framework (MAF) 1.26.0. Existing Gloaming
cores gain no AI dependencies. MAF owns agents, graph execution, native messages, sessions,
checkpoint serialization and external-request correlation. These packages add native EF
persistence and bounded in-process lifecycle helpers; they select no model vendor.

| Package | Responsibility and dependencies |
| --- | --- |
| [Agents.EntityFrameworkCore](Gloaming.Agents.EntityFrameworkCore/README.md) | Native `ChatHistoryProvider`, immutable working-history versions; MAF abstractions and EF Relational, no workflow dependency. |
| [Agents.Workflows](Gloaming.Agents.Workflows/README.md) | Typed native start/resume helpers and JSON checkpoint-manager construction; MAF Workflows, no EF. |
| [Agents.Workflows.EntityFrameworkCore](Gloaming.Agents.Workflows.EntityFrameworkCore/README.md) | Native `ICheckpointStore<JsonElement>`; MAF Workflows and EF Relational. |
| [Conversations.EntityFrameworkCore](../Gloaming.Conversations/Gloaming.Conversations.EntityFrameworkCore/README.md) | Independent visible conversation envelopes; EF Relational, no MAF. |

## Composition and proof

The [independent document-editor consumer](samples/AgentPersistenceDemo/README.md) configures
one module-owned context/schema and commits checkpoints, history, visible entries and its
business-selected head together. The [native PostgreSQL tests](tests/AgentPersistenceTests/AgentPersistenceTests.csproj)
exercise restoration, branching/isolation, tool protocol, reduction, rollback, database grants,
tracked-save guards, writer serialization, multiple pending ports, failure and cancellation.
The [package proof](../../tools/release/README.md#native-agent-persistence) compiles the same
consumer from NuGet without source project references and checks each package independently.

Consumers own admission, authorship, trusted opaque partition/conversation keys, workflow
definitions/versions, selected heads, model configuration, quotas, schema/migrations and grants.
An AI agent is a native execution component; it is not Gloaming's actor attribution or an
authorization rule. Private model context is not automatically visible history or business audit.

EF mappings avoid provider-specific SQL. PostgreSQL 18.6 with EF 10.0.13 is the verified provider;
other providers need their own proofs. Persist native payloads as text without JSON reordering.
Full snapshots trade storage for exact historical restoration. Retention/pruning, incremental
history, background scheduling, durable hosting, automatic retries and exactly-once external
effects are outside this contract. Pre-v1 data conversion/backfills are not supplied.
