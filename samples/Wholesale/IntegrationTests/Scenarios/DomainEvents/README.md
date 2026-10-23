> This is retained integration-test scenario documentation. The former standalone product
> executable was retired by S1. Use the [Wholesale product](../../../README.md) and its
> canonical API/worker/maintenance roles; this directory owns fixture/journey support only.
> Run proofs with `dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj`.

# Registered local domain reactions

The consumer submits immutable Sales drafts and reserves their totals against a separate customer
allowance in one native Sales transaction. Sales owns DraftSubmitted, domain rules, mapping and
ReserveCustomerAllowance. Gloaming supplies local dispatch and shared save composition.

The journey opens allowance 100 EUR and drafts 40/70 EUR, submits the first, rejects the second,
then edits the second to 50 in a fresh operation and submits it. Final reservation is 90 at version 3.

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

Tests apply migrations in PostgreSQL 18.6/Testcontainers; see [development setup](../../../../../docs/development.md).
No broker, HTTP host or background worker is required.

## Composition

[AddSalesDatabase](../../../modules/Sales/Sales/SalesDatabase.cs) registers native context options with
ConfigureGloamingCapabilities, domain handlers and the tenant/audit/inbox/outbox/workflow validators.
The model declares mapped requirements. All Sales compositions use the same database integration,
including profile-only hosts; handler construction remains deferred until a fact needs handling.
AddDraftOrderCommands registers application commands. SalesDbContext has no event dependency,
save override or module-local interceptor.

DraftOrderCommands maps accepted immutable decisions to tracked rows and raises their facts through
IEventSink<SalesModule>. The domain has no queue or readiness checks. The required handler
loads allowance, decides and maps without saving. Shared preparation drains downstream waves,
detects changes and runs validators before one native save. Tenant/actor presence remains sample policy.

## Saving and recovery

Callers begin transactions, execute commands, save, then commit. Both native paths exist; no EF Stage,
owned transaction, acknowledgment or alternate tracker is provided. Each batch clears before handling.
Repeated saves handle only new facts. SaveChanges(false) requires native AcceptAllChanges after success.
Any failure requires rollback/disposal and a fresh decision in a fresh scope. Native write/commit errors
have no universal same-object reuse guard; successful save does not prove subsequent caller commit.

DraftOrderFailures.IsVersionConflict identifies native concurrency on drafts or allowances.
OrderAllowanceExceededException represents a rejected domain reserve decision. Unrelated constraints
remain native faults.

Domain methods enforce invariants and native version predicates arbitrate legitimate competing writes.
Arbitrary row changes, child-only writes and direct SQL bypass decisions; no automatic root-version
advancement or fact/row mirror is inferred. Existing tenant validation still checks handler-created
writes. Late persistence facts, external effects, same-context retries and uncertain commit recovery
are outside local dispatch. Durable delivery is separately selected through Messaging;
the template generator is retired.

See the [package contract](../../../../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/README.md)
and [current report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/ef1-1-capability-composition.md). Earlier reports retain
dated results for superseded implementations.
