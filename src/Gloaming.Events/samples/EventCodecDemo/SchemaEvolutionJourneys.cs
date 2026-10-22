using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo;

public static class SchemaEvolutionJourneys
{
    public static void Run(string fixtureDirectory, TextWriter output)
    {
        var codec = PurchaseOrderSchemaEvolutionExample.CreateCodec();
        string directory = Path.Combine(fixtureDirectory, "Purchasing");
        var order = PurchaseOrderSchemaEvolutionExample.Read(
            FixtureEvents.Read(
                codec,
                Path.Combine(directory, "drafted.v1.json"),
                Path.Combine(directory, "line-set.v1.json")
            )
        );
        var current = FixtureEvents
            .Read(codec, Path.Combine(directory, "line-set.v3.json"))
            .Single();
        var written = codec.Serialize(current);
        var replaced = PurchaseOrderSchemaEvolutionExample.Read(
            FixtureEvents.Read(
                codec,
                Path.Combine(directory, "drafted.v1.json"),
                Path.Combine(directory, "line-set.v1.json"),
                Path.Combine(directory, "line-replaced.v1.json")
            )
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"purchasing schemas: v1-to-v3-total={order.Total:F2}, write-schema={written.SchemaVersion}, replaced-total={replaced.Total:F2}"
            )
        );
    }
}
