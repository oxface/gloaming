# Gloaming.Auditing

Explicitly stage consumer-selected audit envelopes in the owning native EF transaction.
[The EF package](Gloaming.Auditing.EntityFrameworkCore/README.md) contains the complete
public surface, setup and obligations. [The transactional contract](docs/transactional-audit.md)
covers lifecycle and deferred capabilities.

`Gloaming.Auditing.EntityFrameworkCore` depends on existing
ActorIdentity values, shared EF save coordination and native EF Relational. It needs no
Tenancy, Persistence, Events, EventSourcing, Messaging, web, transport, hosting or provider package. Native DI remains
consumer setup; no registration abstraction or unused DI package is added.

Use `ConfigureAudit` in the owning DbContext and select the native provider mapping, for
example `audit.Property(row => row.Details).HasColumnType("jsonb")` with PostgreSQL. The
relational mapping includes the tenant/type/item/time/ID timeline index by default.
Ownership and native index customization remain consumer configuration.

Executable consumers are the Wholesale Sales profile mutation and Inventory inbox stock
issue. The independent `tests/AuditPostgresTests` executable adopts the same interface in an
ordinary tenantless EF context with custom mapping. Run it from the repository root:

```bash
dotnet test --project src/Gloaming.Auditing/tests/AuditPostgresTests/AuditPostgresTests.csproj
```

PostgreSQL is the exercised provider. [E9 execution evidence](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e9-explicit-transactional-audit.md)
is separate from archived audit evidence. Classification, authorization, required-audit
selection, disclosure, query visibility and retention remain consumer-owned.

The sample's scoped `SalesAudit` and `InventoryAudit` wrappers capture actor/tenant from the
established accessors, generate the entry ID, sample the clock and classify the minimal
business payload. Named V1 payload records keep their schema constants beside their fields;
business callers supply only the accepted change. The audit envelope has no
correlation, causation or trace fields; diagnostic telemetry and messaging metadata remain
separate. The explicit library envelope is usable independently of the sample conventions.
