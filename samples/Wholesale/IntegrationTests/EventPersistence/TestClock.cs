using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.EventPersistence;

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
