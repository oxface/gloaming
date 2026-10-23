# Wholesale integration proofs

One product test executable owns API, module and worker integration:

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

Use the repository .NET SDK, reachable PostgreSQL/RabbitMQ through Testcontainers, and the
[local infrastructure instructions](../../../docs/development.md). The project links each
neutral PostgreSQL/RabbitMQ/process helper once. Native telemetry cases run in a nonparallel
collection because their exporters observe process-wide sources; other capability classes
retain ordinary xUnit parallelism.

| Folder | Retained obligation |
| --- | --- |
| `Api` | Native authentication/admission/antiforgery, membership concurrency, profiles/audit, private draft permissions, restricted HTTP credentials/RLS, historical Access migration and telemetry. New product draft lifecycle/allowance rollback assertions live here. |
| `Domain` / `DraftOrders` | Immutable Sales profile/draft decisions, relational children, root concurrency, complete candidate acceptance and rollback; event-sourced decisions live with Inventory. |
| `Reactions` | Required local allowance reactions, save/commit distinctions, cancellation, faults and rejected proposals. |
| `Queries` | SQL filtering/joins, complete permitted counts/pages, caller-owned transaction snapshots and independent restricted-role RLS lifecycle proofs. |
| `EventPersistence` | Literal histories, codecs/upcasting, exact native reads, append/rebuild/regeneration, inbox/outbox faults, real broker assertions, cancellation/process death/ambiguous commit. |
| `Reservations/Broker` | Original stock-issue and compensation behavior, RabbitMQ/process recovery, Sales reply RLS/dead letters and restricted outbox dispatch/retention/migration assertions. |
| `Reservations/Local` | Broker-free public intake, separate transactions, observation and the three real process-death handoff cuts. Child processes explicitly select `Transport=local`. |
| `Scenarios` | Finite fixture/journey helpers retained for active module proofs. They call live module Contracts; no copied product implementation. |

`TestEntryPoint` selects only the two historical child scenarios (`--scenario history` and
`--scenario schema-evolution`) before delegating ordinary execution to the installed xUnit/MTP
runner. This preserves the original actual process assertions without shipping another product
console. Their database comes from `WHOLESALE_SCENARIO_CONNECTION`; they have a 60-second deadline.
Normal tests never route to these branches accidentally. Canonical product commands have separate
real-process coverage in `ProductOperationsTests`, and regeneration launches the real Worker.

The Access SQL fixture stays byte-for-byte intact. Event fixture JSON remains owned by the
independent Events consumer and is linked read-only into the output directory. Retained module migrations
remain in their owning modules. Purchasing and its owned migration chain are retired; the
[current audit](../../../docs/reports/gloaming-library-audit.md) records replacement proof coverage. The [slice report](../../../docs/reports/s1-wholesale-consolidation.md)
maps the old projects and records fresh results separately from earlier slice evidence.

`Directory/` retains fourteen B2 population, transaction and wire proofs. API/Worker process adoption and broker/local route-selection tests prove the canonical product path.
