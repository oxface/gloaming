# Opt-in PostgreSQL tenant isolation

Run the existing CQRS executable with `--tenant-isolation` to exercise actual PostgreSQL
RLS under a separate restricted runtime login. This mode prepares a **fresh disposable
database**, seeds two Organizations and installs policies. It is finite proof setup, not
an idempotent deployment initializer. Ordinary CQRS, HTTP, AppHost and worker startup do
not install or use these policies/credentials.

## Run

Provision two new logins and a new database through an administrative PostgreSQL 18 connection.
For example, execute these statements with `psql` using local disposable credentials:

```sql
CREATE ROLE wholesale_rls_owner LOGIN PASSWORD 'owner-local'
    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
CREATE ROLE wholesale_rls_runtime LOGIN PASSWORD 'runtime-local'
    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
CREATE DATABASE wholesale_rls_demo OWNER wholesale_rls_owner;
REVOKE ALL ON DATABASE wholesale_rls_demo FROM PUBLIC;
GRANT CONNECT ON DATABASE wholesale_rls_demo TO wholesale_rls_runtime;
```

Run from the repository root using the pinned .NET SDK:

```bash
WHOLESALE_MIGRATION_CONNECTION_STRING='Host=localhost;Database=wholesale_rls_demo;Username=wholesale_rls_owner;Password=owner-local;Pooling=false' \
WHOLESALE_RUNTIME_CONNECTION_STRING='Host=localhost;Database=wholesale_rls_demo;Username=wholesale_rls_runtime;Password=runtime-local;Pooling=true;Maximum Pool Size=1' \
  dotnet run --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj -- --tenant-isolation
```

Expected output:

```text
wholesale-alpha: database rows=1, Alpha's permitted drafts=1.
wholesale-beta: database rows=1, Alpha's permitted drafts=0.
```

The migration owner applies existing **Sales** migrations and finite rows before policies
are enabled; it then installs RLS and grants the runtime's table privileges. Both connections
must target that same fresh database. The runtime connection is separately authenticated,
not an administrator temporarily using SET ROLE. Setup rejects unsafe role attributes and
runtime role memberships. The executable verifies the actual runtime/session user, schema
privilege, table ownership/RLS flags and empty tenant baseline before its operations.
This is a bounded setup assertion, not continuous policy/role-drift attestation.

## Exact policy and privileges

[`SalesRowSecurity`](SalesRowSecurity.cs) installs one `FOR ALL TO PUBLIC` policy named
`organization_isolation` with ENABLE and FORCE on each table below:

```sql
USING (organization_key = NULLIF(current_setting('app.tenant_key', true), ''))
WITH CHECK (organization_key = NULLIF(current_setting('app.tenant_key', true), ''))
```

| Sales table | Runtime table privileges |
| --- | --- |
| `customer_profiles` | SELECT, INSERT, UPDATE, DELETE |
| `customer_addresses` | SELECT, INSERT, UPDATE, DELETE |
| `draft_orders` | SELECT, INSERT, UPDATE, DELETE |
| `draft_order_lines` | SELECT, INSERT, UPDATE, DELETE |
| `draft_order_grants` | SELECT only |
| `command_receipts` | SELECT, INSERT only |

The role gets schema USAGE and no Sales-schema CREATE, TRUNCATE, ownership or grant writes.
The four business-table write grants allow the native DML proofs; production consumers
should select privileges for their own actual paths. No privileges are granted on Inventory,
allowances, audit, inbox, outbox, stream or workflow tables.

Receipt proofs also replay an authorized submission retained by an administrative setup
transaction. Runtime cannot replace/delete the result or insert foreign-Organization receipts;
disabled EF filters still reveal only the admitted Organization. This adds no runtime allowance
write privilege or general submission capability to this restricted profile.

USING suppresses foreign existing rows; WITH CHECK rejects proposed foreign ownership.
Missing/empty settings yield no matches and writes fail. Whitespace/unrecognized text also
matches no seeded Organization. Keys remain opaque strings: no UUID cast, trimming or
UUID-validation semantics are added. A selected tenant is not checked against Access by SQL.
See [PostgreSQL policies](https://www.postgresql.org/docs/18/sql-createpolicy.html).

## Native operation protocol

[`TenantIsolationJourney`](TenantIsolationJourney.cs) uses finite trusted demo identities.
A real host authenticates and admits the operation before publishing immutable actor/tenant
context, then explicitly follows this sequence on the protected Sales DbContext:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
await database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", cancellationToken);
// Protected reads or accepted Sales proposals, then SaveChanges if required.
await transaction.CommitAsync(cancellationToken);
```

The [optional Tenancy/PostgreSQL utility](../../../../../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
requires a selected tenant and an open native transaction on the exact Npgsql connection,
then executes parameterized `set_config(@setting, @tenant, true)`. This consumer explicitly
passes `app.tenant_key` to match its policies. The utility does not save,
commit, retry, authenticate, admit membership or alter an operation context. A protected
read also needs an explicit transaction; autocommit local setup cannot authorize a later
statement. Begin and establish again for every new transaction/physical connection/attempt.

Npgsql physical pooling is enabled in the proofs. Connection reset must remain enabled;
No Reset On Close, multiplexing and connection Options are rejected here. DbContext pooling,
external poolers and shared/concurrent DbContexts are not exercised. A rollback to a savepoint
before setup removes the local setting; establish it again before protected SQL. The retry
proof captures one operation's immutable actor/tenant facts, creates a fresh scope/transaction
for each database attempt and establishes SQL context inside the execution-strategy delegate.
No default retry or ambiguous-commit/idempotency protocol is added.

## What this protects

RLS restricts tenant rows even through raw SQL, disabled EF query filters and native bulk DML
under this role/connection/transaction. Sales' existing Read/Edit grant predicates still
decide which drafts the actor can read/edit **within** that Organization. Native SQL can
read other same-tenant drafts without Sales grants, and direct writes can bypass business
decisions/versioning/audit; the policy does not reproduce the Sales Contract.

The executable supports draft reads; proofs also commit an existing-draft line edit.
Creation requires creator-grant insertion/Inventory access, and submission needs allowances
and its required reactions. Those flows are not adopted with these credentials. Workers
need separate intake/claim/handler/completion designs: business setup cannot cover SQL that
runs earlier or in another transaction. Default HTTP/AppHost credentials remain unchanged.

Superusers and BYPASSRLS bypass policies. FORCE subjects ordinary owner reads to policies,
but the owner retains DDL authority. A runtime credential holder can forge `app.tenant_key`;
it is trusted application input, not database authentication. Constraints can reveal hidden
row existence, and session-level settings can reappear after a local setting ends. These
limits are exercised, not assumed away. Read the [protection matrix and results](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/a2-2-sales-postgres-rls.md)
and [pinned primary-source analysis](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/tenant-authorization-primary-sources.md).

## Verify

The existing Wholesale CI CQRS lane automatically includes these tests; no additional
package, project or service is required. The fixture creates distinct role/database names
and cleans them up. On this workstation:

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --filter-class '*TenantIsolationTests'
```

The [A2.3 extraction](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/a2-3-postgres-tenant-context.md) now supplies
the explicit setup utility. Its public extension is used directly by the executable and
the transferred lifecycle proofs. Table inventory, policies, role provisioning and grants
remain sample decisions; automatic transaction interception and default HTTP/worker adoption
remain separate requirements.
