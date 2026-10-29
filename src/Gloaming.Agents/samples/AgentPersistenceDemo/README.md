# Reconstructing document editor

An independent deterministic consumer of the four selected agent/conversation packages.
It uses a document-editor graph with native MAF agent binding and review request/response ports;
it imports no Umbrine DTOs, authorization or module policy and makes no paid inference calls.

The sample's [context](DemoContext.cs) maps the three capabilities and its own selected document
head into schema `documents`. Both native save paths invoke the selected guards. The
[journey](AdoptionJourney.cs) starts a typed input, disposes graph/context/agent state, restores
and refines, then reconstructs and accepts without another model call. Visible proposals retain
their explicit schema version and survive working-context changes. A native transaction commits
all retained stores and the consumer-selected head/business result together.

```sh
# Supply only a disposable empty database through the environment.
dotnet run --project src/Gloaming.Agents/samples/AgentPersistenceDemo/AgentPersistenceDemo.csproj
```

`GLOAMING_AGENT_TEST_CONNECTION_STRING` is required. `EnsureCreated` is explicit disposable
proof setup; production modules own versioned migrations/provisioning and runtime grants.
The sample uses a fixed PostgreSQL transaction advisory lock namespace for its one document.
Real consumers choose complete lock keys, admission and contention behavior, and account for
holding a native transaction/connection across inference. This sample is not a deployment model.

The [owning tests](../../tests/AgentPersistenceTests/AgentPersistenceTests.csproj) add isolation,
older-history branching, native tools/reduction, atomic rollback, grants/guards, concurrency,
pending-request type correlation, provider failure and active cancellation. Run:

```sh
dotnet test --project src/Gloaming.Agents/tests/AgentPersistenceTests/AgentPersistenceTests.csproj
```

Tests create/remove their own PostgreSQL 18.6 container through Testcontainers. Package-only
adoption additionally compiles these consumer sources against the produced NuGet archives;
see the [release proof](../../../../tools/release/README.md#native-agent-persistence).
