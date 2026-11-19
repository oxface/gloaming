using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Gloaming.Events.Serialization;
using Gloaming.Samples.Wholesale.EventCodecDemo;
using Gloaming.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace Gloaming.Events.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class EventDecodeBenchmarks
{
    private readonly JsonEventCodec<IPurchaseOrderEvent> codec =
        PurchaseOrderSchemaEvolutionExample.CreateCodec();

    private SerializedEvent current = null!;
    private SerializedEvent previous = null!;
    private SerializedEvent historical = null!;

    [GlobalSetup]
    public void LoadFixtures()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        current = FixtureEvents.ReadEnvelope(Path.Combine(fixtures, "line-set.v3.json"));
        previous = FixtureEvents.ReadEnvelope(Path.Combine(fixtures, "line-set.v2.json"));
        historical = FixtureEvents.ReadEnvelope(Path.Combine(fixtures, "line-set.v1.json"));
    }

    [Benchmark(Baseline = true)]
    public object CurrentSchema() => Decode(current);

    [Benchmark]
    public object OneUpcast() => Decode(previous);

    [Benchmark]
    public object TwoUpcasts() => Decode(historical);

    private IPurchaseOrderEvent Decode(SerializedEvent envelope) =>
        codec.Deserialize(envelope.EventName, envelope.SchemaVersion, envelope.Payload);
}
