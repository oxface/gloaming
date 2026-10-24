# Literal receipt codec and upcasting example

One BCL-only consumer of Events.Serialization reads independently authored v1 and v2 receipt
JSON through an explicit registry, upgrades the old field name and writes schema 2. There is
no history, database, DI, host, context-library or Wholesale dependency in the executable.

```bash
dotnet run --project src/Gloaming.Events/samples/EventCodecDemo/EventCodecDemo.csproj
```

Expected output: `receipt: v1=10.125, v2=10.125, write-schema=2`.

[CodecExample](CodecExample.cs) supplies its DTO, native JSON options and `quantity` →
`receivedQuantity` step. The alias stays `inventory.stock-position.received`; the v1/v2
[literal files](Fixtures/Inventory) remain unchanged. The consumer parses its envelopes
before decoding. Upcasting applies on reads; serialization writes the declared current shape,
without changing the original stored envelope.

The [adoption suite](../../tests/CodecAdoptionTests/EventCodecDemo.Tests.csproj) owns the older
stock/purchasing folds, bounded history readers, chained/nested schema changes and alias
replacement recipes. Those provide compatibility variants without becoming more advertised
applications. Historic schemas and numeric expected outcomes remain independently authored.
The original literal paths/provenance remain in [Fixtures](Fixtures/README.md), including
payloads linked into Wholesale tests. The
[explicit schema-evolution recipe](../../tests/CodecAdoptionTests/Fixtures/SchemaEvolutionJourneys.cs) is still tested.

```bash
dotnet test --project src/Gloaming.Events/tests/CodecAdoptionTests/EventCodecDemo.Tests.csproj
```

The suite also launches the actual focused executable. For unknown/invalid schemas, native
JSON behavior and extension limits, read the [serialization contract](../../Gloaming.Events.Serialization/README.md).
History remains an independently selected [library](../../Gloaming.Events.History/README.md),
referenced by the adoption tests rather than this executable.
