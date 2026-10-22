# FoundryApplication

Consumer-owned .NET 10 / PostgreSQL application, rooted at `ConsumerRoot`. Catalog owns
the editable `ICatalogCommands`/`ICatalogQueries` contracts and its state-stored items. The host supplies
ordinary native DI composition. There is no runtime dependency on the creation tool.

## Build and run

```sh
dotnet build FoundryApplication.slnx
dotnet run --project src/FoundryApplication.Host
```

The default command only prints instructions. It does not connect, migrate, seed, start
workers or establish an actor/tenant. Invalid commands and missing migration configuration
exit with code 2. This is a small adoption executable, with no production business ingress.

Provision a PostgreSQL 18.6 database separately; supply its connection string through
`CATALOG_CONNECTION_STRING`. Then explicitly apply the module-owned migration:

```sh
dotnet run --project src/FoundryApplication.Host -- migrate
```

This creates only `catalog.items` and `catalog.__EFMigrationsHistory`, and seeds no data.
The initial migration is retained; a forward migration adds price and version to existing
items with defaults zero and one. No existing names/owners are rewritten.
The module's native provider registration, schema, migration and history location are editable.
To scaffold a later change, restore the pinned native EF tool and use the design-time factory:

```sh
dotnet tool restore
dotnet ef migrations add YourChange --project modules/Catalog/FoundryApplication.Catalog --startup-project src/FoundryApplication.Host
```

## Executable Catalog journey

Set `CATALOG_TEST_ADMIN_CONNECTION_STRING` to a **disposable local PostgreSQL server** with
permission to create/drop databases. The tests create a fresh database, run the real host
before initialization, explicitly migrate, seed two tenants with native saves/transactions,
and call `ICatalogQueries`. They drop their own database in `finally`.

```sh
dotnet test --project tests/FoundryApplication.Adoption.Tests
```

The journey checks exact module tables, migration/model consistency, no implicit startup
initialization or migration seeding, tenant-isolated reads, rejected foreign inserts, and a
detached forged-owner update rejected by the stored-owner concurrency predicate. Four
additional cases reject missing actor, missing tenant, anonymity and deliberate tenantless
execution before database access. Further tests exercise the real command/query contracts,
filtered count/paging, invalid complete candidates, rollback and competing same-tenant edits.

## Catalog decisions and reads

Catalog's small consumer-owned rule is a nonblank name of at most 200 characters and price
from zero through 10,000, with at most two decimal places. `CatalogItemState` validates an
immutable complete candidate before persistence changes. `ICatalogCommands.CreateAsync`
and `ChangeAsync` stage native rows; `Changed` is proposed state only. The caller begins a
native Catalog transaction, invokes one operation, then explicitly saves and commits or
rolls back. Commands never publish or start transactions. Invalid candidates throw before
tracking changes. A missing visible item returns NotFound; a stale expected version returns
Conflict. Native SaveChanges can still throw DbUpdateConcurrencyException when another
operation commits after the load. Roll back and dispose the failed scope; retry/redecision
is explicit consumer policy. The module does not prevent privileged raw/bulk writes.

`ICatalogQueries.SearchAsync` uses module-local `CatalogFilters` to apply case-sensitive
literal name substring and minimum price before count and price/ID order plus offset/limit
(1–100). Reads are untracked and return immutable Contracts. Count and page are separate
statements; a caller requiring one snapshot begins its own repeatable-read transaction.
Ordering does not stabilize separate page requests. There is no generic repository,
mediator, execution pipeline or automatic transaction owner.

The **test-only** system identities and tenant selection represent already completed
consumer authentication/admission. They are not an authentication implementation. Before
exposing business operations, the consumer must authenticate callers or trusted work,
map canonical identities, admit the selected tenant, authorize the capability, initialize
both segments once in a fresh operation scope, and only then invoke the contract. The host
contains no request-supplied identity path or demo authentication scheme.

## Library adoption and obligations

`libraries/` contains local source snapshots of four existing libraries, with their stable
namespaces unchanged. `library-sources.json` records SHA-256 hashes of every copied source
and project file. `foundry.json` records creation configuration. All project references
stay inside this repository; copied project references are adjusted to this flat directory layout.
C# source is unchanged. Shared EF infrastructure is a Persistence dependency; Catalog retains its
explicit save validation without domain-event or messaging activation. External NuGet restore is
still required. Nothing is published.
Review/update these snapshots explicitly; automatic upgrades and package distribution are
future work. Library source receives the generated root build/package settings.

ActorIdentity and Tenancy are independent package-free immutable scoped contexts. Presence
checks grant no authority. Catalog wires separate read/initialization interfaces to the
same scoped holder and requires both established contexts before reading. Establishment
must finish before parallel business work; DbContext itself is not thread-safe.

EF ownership supplies a named filter, tracked-write validation and a stored-owner concurrency
predicate. `CatalogDbContext` explicitly calls validation from both save overrides; the
consumer supplies owner values and owns the final save/commit. Ordinary ownership changes
are rejected. Only ordinary unshared single-table string ownership is used here. Bulk/raw
SQL, disabled query filters, identity-map lookups, privileged operations and arbitrary
same-process code need consumer safeguards. Catalog's separate Version token detects
competing same-tenant edits through the command protocol. Ownership alone does not provide
that version policy. No cross-module transaction is demonstrated.

Authentication, tenant admission, business Contracts, database provisioning, seeds, saves,
transactions, retries, cancellation/failure presentation and any exposed transport remain
consumer responsibilities. There are no event-sourcing or messaging packages, registrations,
migrations in this composition. HTTP adapters, Access lifecycle, OIDC, Aspire,
provider options and repository updates are not supported creation choices.

<!--#if (CatalogWorker)-->
## Selected separate Catalog report worker

This creation explicitly selected a separate `FoundryApplication.CatalogWorker` executable.
It uses native .NET hosting and the same editable composition/Contracts, with no queue or
transport. The ordinary Host never starts it. After explicitly migrating, run it with a
trusted deployment mandate selecting an admitted tenant:

```sh
CATALOG_CONNECTION_STRING='your PostgreSQL connection' \
CATALOG_WORKER_TENANT='your-admitted-tenant' \
dotnet run --project src/FoundryApplication.CatalogWorker -- --once
```

Without `--once` it reports periodically until native host shutdown. Optional
`CATALOG_WORKER_INTERVAL_SECONDS` is 1–3600, default 30. Each iteration creates a fresh scope,
establishes the fixed system actor `catalog-report-worker` and configured tenant, and calls
the Catalog search Contract. It prints the current local count and first page size (at most
100). This is a report, not a full export or durable job. Deployment authorization for the
tenant/system mandate belongs to the consumer; configuration is never accepted from an
HTTP caller. Missing mandate/configuration exits 2, query failure exits 1 without a successful
report, and successful `--once` exits 0. Startup never migrates/seeds. The consumer owns
database role privileges and operation authorization before production use.

The selected adoption tests start actual worker processes for both tenants, repeat two
iterations to exercise fresh context establishment, then send SIGTERM and await exit. They
also exercise missing mandate and unavailable database. No durable queue, exactly-once
execution, restart cursor, broker semantics or general shutdown bound is claimed.
<!--#endif-->
