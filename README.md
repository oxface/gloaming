# Gloaming

Gloaming provides independently selectable .NET libraries
for native APIs and workers. Consumers own application composition, domain rules, identity
admission, persistence setup, transport routing and deployment. Explicitly selected library
operations may own a bounded transaction and invoke consumer handlers under their documented
contracts.

Start with a selected family's setup and guarantees. A family folder groups packages,
documentation and tests; it does not introduce an umbrella dependency or application runtime.

| Family | Packages and purpose |
| --- | --- |
| [Evaluations](src/Gloaming.Evaluations/README.md) | Typed evaluation sets across ordinary code, agents and workflows, with native metrics and bounded execution. |
| [Agents](src/Gloaming.Agents/README.md) | Optional native MAF history/checkpoint persistence and typed in-process lifecycle helpers. |
| [Conversations](src/Gloaming.Conversations/README.md) | MAF-free ordered visible envelopes with explicit consumer payload types and versions. |
| [ActorIdentity](src/Gloaming.ActorIdentity/README.md) | Executing actor/initiator context and optional native ASP.NET Core integration. |
| [Tenancy](src/Gloaming.Tenancy/README.md) | Selected-tenant/tenantless context, optional HTTP admission and explicit PostgreSQL transaction context. |
| [Persistence](src/Gloaming.Persistence/README.md) | EF ownership filtering/protected writes and optional PostgreSQL RLS. |
| [EntityFrameworkCore](src/Gloaming.EntityFrameworkCore/README.md) | Explicit preparation/validation composition through native EF saving. |
| [CommandReceipts](src/Gloaming.CommandReceipts/README.md) | Logical action/result retention and cooperating-writer serialization in caller-owned EF/PostgreSQL transactions. |
| [Events](src/Gloaming.Events/README.md) | Independent JSON serialization/upcasting and event-history utilities. |
| [EventReactions](src/Gloaming.EventReactions/README.md) | Local event buffers, ordered reactions and optional EF preparation. |
| [EventSourcing](src/Gloaming.EventSourcing/README.md) | Aggregate bookkeeping, optional native EF write/rebuild operations and PostgreSQL committed feed/secondary projection generations. |
| [Auditing](src/Gloaming.Auditing/README.md) | Explicit accepted-change envelopes and transactional EF staging. |
| [Messaging](src/Gloaming.Messaging/README.md) | Incoming/outgoing envelopes, callable inbox/outbox roles, optional polling hosts, PostgreSQL delivery and bounded continuations. |

## Use Gloaming

For rapid changes across repositories, pack unique local NuGet versions:

```bash
npm --prefix tools/release run pack
```

The command reports the completed version and folder feed. Follow the
[consumer source-mapping setup](tools/release/README.md#consume-from-another-repository) and
[development guide](docs/development.md); no remote package service is required.
The heavier release verifier checks all archives and isolated package-only consumers, including
native agent reconstruction and independent selection of each agent/conversation package.

[Modular monolith patterns](modular-monolith-patterns/README.md) describe how to initialize
or adapt an application and add named modules, state-stored or event-sourced aggregates,
identity setup and durable handoff. Give an agent that folder and the requested module map;
the documents supply the workflow and completion checks.

## Examples and proofs

- [Workflow evaluations](src/Gloaming.Evaluations/samples/WorkflowEvaluationDemo/README.md): one typed set against deterministic and agent workflows, plus recorded output scoring.
- [Native agent persistence](src/Gloaming.Agents/samples/AgentPersistenceDemo/README.md): deterministic document-editor reconstruction, refinement and acceptance in one consumer-owned transaction.
- [Native actor/tenant composition](src/Gloaming.Tenancy/docs/composition.md): explicit DI scopes and independent context establishment, without a business sample.
- [Ownership](src/Gloaming.Persistence/samples/PersistenceDemo/README.md): module-owned contexts/migrations, protected writes and ordinary native transactions.
- [Receipt codec/upcasting](src/Gloaming.Events/samples/EventCodecDemo/README.md), [counter append](src/Gloaming.EventSourcing/samples/EventStorageDemo/README.md) and [immutable-model reactions](src/Gloaming.EventReactions/samples/EventReactionsDemo/README.md): focused family adopters.
- [Durable delivery](src/Gloaming.Messaging/samples/Delivery/README.md): independent module libraries, producer API and separately selected worker roles.
- [Wholesale](samples/Wholesale/README.md): Access, Sales and Inventory compose the cross-family reference application, with consumer-owned business policy and real runtime/browser proofs.

Fault injection and separately compiled deployment consumers belong to tests, including
[Messaging fixtures](src/Gloaming.Messaging/tests/Fixtures/README.md). Retained historic payloads
are independent test data, not values manufactured by the current serializer.

## Develop

Use the .NET SDK pinned by `global.json`, Node 24 for repository/release tools, and a
Docker-compatible runtime for provider/process tests. Rootless Podman works through its
user socket.

**[Find and run tests](tests/README.md):** library suites live under each
`src/Gloaming.*/tests` directory. Root `tests` covers cross-family composition and architecture;
Wholesale owns its application integration/runtime suites. The solution includes all of them.

```bash
dotnet tool restore
dotnet build Gloaming.slnx
dotnet test --project src/Gloaming.EntityFrameworkCore/tests/CapabilityTests/CapabilityTests.csproj --no-build --no-restore
dotnet test --project tests/ContextCompositionTests/ContextCompositionTests.csproj
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj
```

Family-local READMEs list their own suites. See the [development commands](docs/development.md),
[repository workflow](docs/conventions/repository.md) and [.NET conventions](docs/conventions/dotnet.md).
CI selects affected families, package adopters and dependent consumers through an explicit path map.

See the [public documentation](docs/README.md), [glossary](CONTEXT.md) and
[test guide](tests/README.md). Current capabilities, setup and supported limits remain beside
their owning libraries.
