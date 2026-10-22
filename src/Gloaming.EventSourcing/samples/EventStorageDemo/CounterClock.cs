namespace ModulithFoundry.Samples.EventStorageDemo;

internal sealed class CounterClock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = DemoData.RecordedAt;
    internal int Calls { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        Calls++;
        return Now;
    }
}
