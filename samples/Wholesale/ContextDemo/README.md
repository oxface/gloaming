# Wholesale context demonstration

A finite console consumer of independent [actor identity](../../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/README.md)
and [tenancy](../../../src/Gloaming.Tenancy/Gloaming.Tenancy/README.md) libraries.
It uses project references and standard Microsoft DI, with no EF, ASP.NET Core, Rebus,
Aspire or Access.Contracts. It supplies demonstration identities explicitly and opens no
HTTP endpoints. Authentication and durable business behavior enter later slices.

```bash
dotnet run --project samples/Wholesale/ContextDemo/ContextDemo.csproj
```

Run that command from the repository root. Expected output:

```text
wholesale-alpha: availability=42
wholesale-beta: availability=7
wholesale-alpha: requested=10, available=42, can-fulfil=True, actor=Human:demo-user-alex, initiator=none
wholesale-beta: requested=10, available=7, can-fulfil=False, actor=System:sales.order-fulfilment, initiator=demo-user-alex
host-info: actor=Anonymous
maintenance: actor=sample.maintenance
```

## Ownership and policy

Inventory owns tenant-keyed fixture quantities and the availability query. The same SKU has
42 units for Alpha and 7 for Beta; unknown tenant/SKU combinations return zero. A selected
tenant is required. Inventory needs only the tenancy accessor; HTTP authentication policy remains future consumer work.

Sales owns a draft-order preview: a positive requested quantity can be fulfilled when it
does not exceed Inventory's available quantity. Sales calls Inventory through its Contracts
interface, sharing the selected tenancy context. Sales requires a selected tenant and identified
actor; initiator attribution does not satisfy the actor requirement. Neither policy is
an authenticated production authorization rule.

Both capability implementations are internal in this demo assembly. Their co-location does
not prove physical module isolation. Fixtures are immutable in-memory sample data; previews
do not reserve stock or persist orders.

## Language

- **Availability**: the sample fixture quantity for a SKU within one tenant.
- **Draft preview**: a read-only assessment of whether a requested quantity fits availability.
- **Executing actor** and **initiator** follow [the project glossary](../../../CONTEXT.md).

## Exercised template recipe

[DemoComposition](DemoComposition.cs) visibly registers each scoped holder and aliases its
reader and initializer to the same instance. It provides actor-only, tenancy-only and combined
service compositions. [Program](Program.cs) owns each operation scope, initializes the segments
required by that operation, resolves capabilities, and disposes the scope. Host information
and maintenance use actor-only services; Inventory availability uses tenancy-only services.
Sales explicitly checks both segments. Combined establishment is a host obligation, without
an atomic publication protocol across the holders. Parallel
availability calls use distinct scopes. Later HTTP/worker consumers can copy this setup and
replace their own identity mapping, tenant admission and capability policy.

[Composition tests](../ContextDemo.Tests/CompositionTests.cs) use the same registration
code and verify independently expected quantities, cross-scope isolation, missing context,
anonymous rejection in Sales, attribution, child scopes and failure/cancellation cleanup.
