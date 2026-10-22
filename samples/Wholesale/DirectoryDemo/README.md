# Customer directory population composition

This temporary executable composes actual Sales public profile mutation/export with a separate
Dispatch-owned directory. The orchestration is intentionally small enough to move into the
Wholesale product Worker. Source and receiver use separate PostgreSQL connections/databases;
relay code calls only Dispatch.Contracts. The [module guide](../modules/Dispatch/README.md)
defines guarantees, readiness, writer participation and remaining limits.

Configure `ConnectionStrings__Sales` and `ConnectionStrings__Dispatch` with PostgreSQL databases.
The native migrations support existing Sales storage and new Dispatch storage. Then run:

```sh
dotnet run --project samples/Wholesale/DirectoryDemo -- --role setup
dotnet run --project samples/Wholesale/DirectoryDemo -- --role seed --Organization wholesale-alpha
# Keep workers running in one terminal; they retry until population admission exists.
dotnet run --project samples/Wholesale/DirectoryDemo -- --role run
# In a second terminal, admit and copy the complete initial population.
dotnet run --project samples/Wholesale/DirectoryDemo -- --role populate --Organization wholesale-alpha
dotnet run --project samples/Wholesale/DirectoryDemo -- --role search --Organization wholesale-alpha --Text Customer
```

`seed` stages historical Alpha/Beta demonstration rows without outgoing messages and is only
for finite initial setup, never live ingestion. This proves why replay alone cannot bootstrap
existing state. Live creation uses `ICustomerDeliveryDirectory.RegisterAsync`; profile edits
use the existing `ICustomerProfiles` API. The executable also offers `edit` (CustomerId,
AddressId, Version, Name, Address), `withdraw`/`relist` (CustomerId, Version), `progress` and
`search` (optional Text) roles. Roles use a named demo human actor and explicit sample tenant;
this is trusted internal composition rather than an authentication/authorization example.

`populate` persists admission before any public source reads, copies bounded pages and exits.
It can restart after interruption; progress survives in Dispatch. `run` starts the existing
Gloaming outbox and inbox workers. Readiness is complete baseline traversal with eventual
updates, not a caught-up barrier. No broker is involved. Combining other Sales outbox routes
requires the product's explicit publisher router; this executable handles the directory route.

Fresh infrastructure proofs:

```sh
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_RYUK_DISABLED=true \
dotnet test --project samples/Wholesale/DirectoryDemo.Tests --no-progress --no-ansi \
  --output Normal --show-stdout Failed --show-stderr Failed
```

The suite starts real PostgreSQL 18.6 containers, migrates separate source/receiver databases,
and exercises actual native transaction interleavings and failed commits, not a mock broker
or in-memory store. The [B2 report](../../../docs/reports/b2-dispatch-directory-population.md)
records the proof matrix and extraction assessment.
