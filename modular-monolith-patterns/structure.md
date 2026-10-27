# Application structure

Use module boundaries as the main organization. Within a module, begin with one implementation
project and a Contracts project when the host or another module needs its business API.
Use folders inside the implementation for domain, application operations and persistence.

~~~text
<Application>.slnx
global.json
Directory.Build.props
Directory.Packages.props
orchestration/
  local/
    <Application>.Local.AppHost.csproj
src/
  <Application>.Server/
  <Application>.Worker/
  <Application>.Migrator/
  <Application>.ServiceDefaults/
  Modules/
    X/
      <Application>.X.Contracts/
      <Application>.X/
        Domain/
        Application/
        Persistence/
          Migrations/
    Y/
      <Application>.Y.Contracts/
      <Application>.Y/
tests/
  <Application>.ArchitectureTests/
  <Application>.IntegrationTests/
docs/
  architecture.md
~~~

Create folders when they contain implementation. Worker and service-default projects have
their selected lifecycle responsibilities; document or omit an unnecessary host rather than
inventing recurring jobs. Existing repositories may use a different layout while keeping the
same ownership boundaries.
Add `orchestration/e2e`, `orchestration/shared` and a publishing AppHost only when used.
The [browser stack profile](dotnet-react-vite.md) adds `apps/`, private workspace `packages/`
and `eng/` helpers for its selected frontend and verification tools.

## Dependencies

- Contracts exposes business operations, inputs and results. Keep EF rows, DbContexts,
  event-store internals and implementation DTOs inside the module implementation.
- A module implementation references its Contracts, selected technical libraries and another
  module's Contracts when needed. It does not reference another module implementation.
- Server/Worker composition roots reference implementations to register native services.
  Business endpoints and cross-module calls use Contracts.
- Migrator invokes explicit module setup entry points for their migrations and grants.
  AppHost references executable hosts for local orchestration.
- Business modules have no dependency on Server, Worker, Migrator, AppHost or Aspire.
- Domain objects contain their own invariants. State-stored aggregates need no shared domain
  base. Event-sourced aggregates may select Gloaming bookkeeping without moving rules into it.

Use names from the target application. Wholesale's Access/Sales/Inventory language is example
policy and is not a prescribed set of modules.

## Persistence ownership

Give every module a native DbContext, owned schema, migration history and named connection.
A shared physical PostgreSQL instance/database is a deliberate deployment choice; it does not
create a shared context or cross-module unit of work. Separate databases/roles are appropriate
when the application's isolation requirements call for them.
Keep native `IEntityTypeConfiguration<T>` mappings with the module alongside migrations.
Hosts supply configuration and select setup operations; modules own schema-specific SQL.

Use business IDs in another module's data rather than cross-module EF navigation or database
foreign keys. Query another module through its Contracts or maintain an explicitly owned
receiving view. Same-process calls that commit independently do not create atomicity.

## Record the result

The target architecture document lists each module, aggregate roots, persistence mode,
schema/database, connection/credential roles, public Contracts and transaction owner.
It also records chosen hosts, identity/tenant admission and any durable delivery boundary.
Keep library API details in their owning library contracts.
