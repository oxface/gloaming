> This is retained integration-test scenario documentation. The former standalone product
> executable was retired by S1. Use the [Wholesale product](../../../README.md) and its
> canonical API/worker/maintenance roles; this directory owns fixture/journey support only.
> Run proofs with `dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj`.

# State-stored and event-sourced domain decisions

A finite executable comparison of Sales customer profiles and Purchasing purchase orders.
Business operations use each module's existing Contracts. Native contexts, migration setup and
finite seed helpers are explicit composition exceptions. No broker, worker, HTTP server or new
DDD library is registered by this executable.

## Run

Requires the repository's .NET 10 SDK and a **fresh disposable PostgreSQL database**. The executable
applies existing Sales and Purchasing migrations and inserts fixed demonstration identities;
it is not repeatable setup for an existing application database.

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

Expected output:

```text
Sales: created v1, committed v2; invalid address rejected.
Purchasing: drafted v1, committed v2 total=37.50 EUR; overflowing total rejected.
```

The sample assumes trusted finite admission for `wholesale-alpha` and `ddd-demo-human`.
Establishing those contexts supplies attribution and tenant scope, not business authorization.
It registers existing library dependencies through the modules, but no messaging processing,
transport or automatic domain-event publication. Sales still maps its existing messaging tables;
this executable is not proof of an independently messaging-free Sales assembly.

## Domain decisions and persistence

| Concern | Sales customer profile | Purchasing purchase order |
| --- | --- | --- |
| Creation | Finite seed setup constructs a complete valid profile at version 1 before tracking either row. This is not a production creation API. | Draft command creates the aggregate and collects its selected draft fact. |
| Domain data | Immutable profile-details value; nonblank display name/address with the existing 256/512-character limits. Accepted text is preserved exactly. | Module-owned line values: positive quantity, nonnegative unit price, replacement by item code. |
| Candidate acceptance | An immutable aggregate returns a complete replacement candidate; the observed aggregate stays unchanged. | Existing EventSourcedAggregate evolves the whole batch, validates its final total and then collects pending facts. |
| Rejection | Invalid details cannot partially replace name/address; stale expected version returns Conflict without a candidate write. | Invalid lines or an overflowing final total preserve accepted state, version and pending facts. A temporarily overflowing intermediate total may be replaced within the batch. |
| Concurrency | Service checks the observed version; native EF original-version UPDATE predicates still arbitrate commits racing after that check. | Existing native event-store version/stamp predicates arbitrate competing writers. |
| Durability | Existing CustomerProfiles service owns save/commit; name, address and explicit audit commit together. | Caller begins the owning transaction, stages the command and explicitly saves/commits. Changed remains a proposal until commit. |

The state-stored aggregate contains no event list, historical replay or inherited technical
base. Domain rules and domain values stay internal to Sales; public Contracts keep their existing
plain DTOs. The service loads native rows, adapts them to observed domain state, and stages the
accepted candidate. Persisted profiles must satisfy the sample's identity/text/version rules;
this path is not a corruption-repair operation. Code uniqueness and same-Organization address
ownership remain native schema/service responsibilities.

The two transaction recipes remain distinct. Do not wrap Sales's service-owned operation in
the Purchasing caller-owned transaction or infer atomicity between modules. Discard a context
after save/commit failure; constructing a valid domain candidate does not establish durability.

## Proofs and extraction outcome

```sh
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

Tests use Testcontainers PostgreSQL 18.6 and disposable databases, plus direct pure-domain proofs.
For rootless Podman use the environment described in [development commands](../../../../../docs/development.md).
Only the proof assembly receives internal access to Sales/Purchasing domain types. The executable
itself uses Contracts and explicit native composition.

The proof suite checks immutable accepted/rejected Sales decisions, value equality, creation
guards and version overflow; Purchasing complete-candidate validation with preservation of
pending facts; real consumer writes, stale/future expected versions, failure after the first
Sales save, recovery in a fresh scope, and committed state versus explicit event history.
The existing HTTP/event suites supply separately rerun regression evidence in
[the D1 report](../../../../../docs/reports/d1-ddd-sample-patterns.md).

**No new reusable mechanism was proven.** This slice demonstrates editable consumer patterns
and uses already reviewed event bookkeeping. Domain identities/values, profile policy and native
row adaptation do not justify another general-purpose base or helper. No T1 template preset,
production creation endpoint, persistent domain-event policy or CQRS dispatcher was added.
