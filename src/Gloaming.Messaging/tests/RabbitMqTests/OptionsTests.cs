using Gloaming.Messaging.RabbitMQ;

namespace Gloaming.RabbitMqTests;

public sealed class OptionsTests
{
    private const long Valid = 5 * TimeSpan.TicksPerSecond;

    [Theory]
    [InlineData(0, Valid, "ProtocolTimeout")]
    [InlineData(-1, Valid, "ProtocolTimeout")]
    [InlineData(long.MaxValue, Valid, "ProtocolTimeout")]
    [InlineData(Valid, 0, "ConnectionCloseTimeout")]
    [InlineData(Valid, -1, "ConnectionCloseTimeout")]
    [InlineData(Valid, long.MaxValue, "ConnectionCloseTimeout")]
    public async Task InvalidPhaseTimeoutsFailBeforeNativeConfigurationOrConnection(
        long protocolTicks,
        long closeTicks,
        string parameter
    )
    {
        var failure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            RabbitMqSession.OpenAsync(
                _ =>
                    throw new InvalidOperationException(
                        "Invalid options must fail before configuration."
                    ),
                new(TimeSpan.FromTicks(protocolTicks), TimeSpan.FromTicks(closeTicks)),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(parameter, failure.ParamName);
    }
}
