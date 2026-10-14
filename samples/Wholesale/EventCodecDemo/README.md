# Standalone two-family event serialization and history consumer

A finite .NET 10 consumer of [Events.Serialization](../../../src/Gloaming.Events/Gloaming.Events.Serialization/README.md).
Its two project references are the independently adoptable serialization and history
libraries; it has no package, database, HTTP, bus, DI or context-library dependency. It
demonstrates explicit event registration, native JSON contracts and bounded history
reconstruction without adopting the full modular-monolith application.

```bash
dotnet run --project samples/Wholesale/EventCodecDemo/EventCodecDemo.csproj
```

Expected output, independent of current numeric culture:

```text
inventory: item=11111111-1111-1111-1111-111111111111, location=22222222-2222-2222-2222-222222222222, unit=EA, on-hand=10.125
purchasing: code=OLD-1, supplier=SUP-1, currency=EUR, total=31.25
inventory history: head=3, current=13.000, at-version-2=10.125, at-cutoff=10.125, before-open=none
purchasing history: head=3, current-total=62.50, at-version-2=31.25, at-cutoff=62.50, before-draft=none
```

## Ownership and recipe

[Inventory](Inventory/StockPositionExample.cs) owns the stock-position example, item/location
identifiers, base unit, quantity interpretation and optional delivery reference. It explicitly
registers opened/received aliases and uses native `JsonRequired` plus nullable enforcement.
`StockPositionReceived` has a different CLR name from archived `StockReceived`; the retained
v1 alias/payload still reads correctly. The new optional delivery reference defaults to null
when absent. That compatibility choice belongs to Inventory, not the codec.

[Purchasing](Purchasing/PurchaseOrderExample.cs) owns draft/line payloads, supplier reference,
currency and line totals. It explicitly registers its two aliases and enables native required
constructor parameters and nullable enforcement. Its local fold replaces a line by exact
item code and calculates quantity times unit price. It does not depend on Inventory's events
or registry.

[FixtureEvents](FixtureEvents.cs) visibly reads/parses local fixture files and extracts the
envelope and clones its native payload before the source document is disposed. [DemoJourneys](DemoJourneys.cs) chooses exact fixture
paths/order, invokes the two consumer-local folds and prints their business results. There
is no automatic event discovery, required domain base class or registration framework.
See [fixture provenance](Fixtures/README.md) for copied versus newly authored literals.

These internal families exercise the library before E5 integrates event-sourced behavior
into the owning modules. They are not additional populated HTTP modules or empty Contracts
projects, and do not change the existing state-stored availability/profile sample. The folds
make decoded fields observable; they are not complete aggregates, sequence/history validators,
pending-event collectors, append operations or reservation/order workflows. Business-rule
completeness, persistence and transaction guarantees require later proofs.

## Explicit selected-range reads

[Inventory history](Inventory/StockPositionHistoryExample.cs) assigns three explicit stream
positions/timestamps to local fixture envelopes. The first receipt leaves on-hand 10.125;
a second receipt of 2.875 produces 13 and a delivery reference. Earlier version/time reads
retain the first quantity and absent reference. [Purchasing history](Purchasing/PurchaseOrderHistoryExample.cs)
replaces the same line from quantity 2.5 to 5, moving total from 31.25 to 62.50. Its two line
events deliberately share a recorded timestamp; inclusive time selection includes both,
while version 2 retains the earlier line.

Consumer-owned [RecordedEvent](RecordedEvent.cs) contains metadata and the native envelope.
Each consumer selects and orders rows, materializes the prefix once, calls
`EventHistory.ValidateRange` on positions/timestamps, decodes with its own codec, then folds.
For time reads, it finds the highest qualifying version within the supplied head before
reading the complete prefix through that version. An empty time prefix maps to no historical
state. Only the returned range is validated; an earlier read can succeed despite later
metadata corruption. Full-stream integrity checking is separate maintenance work.
[HistoryJourneys](HistoryJourneys.cs) runs these recipes through the actual executable.
Repeated reconstruction starts fresh state and preserves source envelopes; there is no
command, pending-event tracking, saving or publishing infrastructure in these folds.

The timestamps and supplied head are authored fixture metadata, not a database capture or
commit-order proof. The history utility checks contiguous ordering and nondecreasing timestamps
within the selected range. It owns no payload generic, snapshot, selector or retained rows.
Its package division remains provisional until E5.2 proves real EF use. E5.2 must still prove module-scoped database loading,
native append, concurrency and caller-owned transactions. This memory-only slice does not
add a full Purchasing module or change the existing HTTP business ingress.

The exercised template material is this editable explicit registration/native JSON recipe
and select/materialize/validate/decode/hydrate composition.
Generated template output remains E10. Sample rules and module Contracts stay outside the
technical library.

## Proofs

```bash
dotnet test --project src/Gloaming.Events/tests/EventSerializationTests/EventSerializationTests.csproj
dotnet test --project src/Gloaming.Events/tests/EventHistoryTests/EventHistoryTests.csproj
dotnet test --project samples/Wholesale/EventCodecDemo.Tests/EventCodecDemo.Tests.csproj
```

The [consumer proofs](../EventCodecDemo.Tests/CompatibilityTests.cs) read actual local literals
and assert independently expected stock identity/quantity and purchase-order fields/totals.
They retain Inventory's required/null rules, Purchasing's constructor-field requirement and
an optional absent delivery reference. One family rejects the other's unregistered alias;
the actual console recipe exercises both. Library proofs cover conflicts, runtime dispatch,
unknown/unsupported identities, payload failures and configuration snapshots. They do not
repeat every native JSON option or test event-store behavior before it exists.

The suites and console run in container-free CI; focused suites also run in commit hooks.
See [the E4 report](../../../docs/reports/e4-event-serialization.md) for fresh verification and
remaining limits. No active build/runtime reference points to the archive.

The [history proofs](../EventCodecDemo.Tests/HistoryCompatibilityTests.cs) add independent
current/historical state expectations, repeat reconstruction, codec failure propagation and
preservation of consumer domain exceptions. [The E5.1 report](../../../docs/reports/e5-1-event-history.md)
records fresh history evidence separately from E4. All three focused suites and the console
run without containers.

## Explicit schema-evolution journey

```sh
dotnet run --project samples/Wholesale/EventCodecDemo/EventCodecDemo.csproj -- --schema-evolution
```

Output:

```text
purchasing schemas: v1-to-v3-total=31.25, write-schema=3, replaced-total=62.50
```

The [separate Purchasing adopter](Purchasing/PurchaseOrderSchemaEvolutionExample.cs) decodes
retained flat v1, renamed flat v2 and nested v3 into the same current fact. Two explicit
consumer-owned transformations select v3; current writes are registered at v3. It reuses the
original sample's domain fold rather than changing line meaning. Independently authored v2/v3
literals and [tests](../EventCodecDemo.Tests/UpcastingCompatibilityTests.cs) assert fields,
strict nested decoding and preserved totals. Currency output is formatted explicitly; numeric
value is preserved, not incidental JSON decimal scale. Original exact-v1 recipes/default output
remain intact. The [codec README](../../../src/Gloaming.Events/Gloaming.Events.Serialization/README.md)
also documents the provided PassThrough step for optional-field additions.
