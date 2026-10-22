using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

internal sealed class TestClock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = FixtureHistories.FirstChangeAt;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class ClockConfiguration
{
    internal static IServiceProvider WithClock(this IServiceProvider provider, DateTimeOffset time)
    {
        provider.GetRequiredService<TestClock>().Now = time;
        return provider;
    }
}
