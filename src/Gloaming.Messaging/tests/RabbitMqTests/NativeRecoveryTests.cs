using System.Text;
using Gloaming.Messaging.RabbitMQ;
using RabbitMQ.Client;

namespace Gloaming.RabbitMqTests;

public sealed class NativeRecoveryTests(RecoveryBrokerFixture rabbit)
    : IClassFixture<RecoveryBrokerFixture>
{
    [Fact]
    public async Task NativeRecoveryRestoresThreeOwnedChannelsAcrossLiveBrokerRestart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        // Like WorkflowBroker: keep the native default automatic recovery, without an observer.
        await using var session = await RabbitMqSession.OpenAsync(
            factory => factory.Uri = new Uri(rabbit.ConnectionString),
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            token
        );
        Assert.Throws<InvalidOperationException>(() =>
            session.ObserveTerminalShutdown(CancellationToken.None, _ => { })
        );
        var commands = await session.CreateChannelAsync(new(true, true), token);
        var replies = await session.CreateChannelAsync(new(true, true), token);
        var intake = await session.CreateChannelAsync(cancellationToken: token);
        string queue = "session-recovery-" + Guid.NewGuid().ToString("N");
        await intake.QueueDeclareAsync(queue, true, false, false, cancellationToken: token);
        string first = Guid.NewGuid().ToString("D");
        await PublishAsync(commands, queue, first, token);
        var closed = new[] { commands, replies, intake }
            .Select(channel =>
            {
                var signal = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                channel.ChannelShutdownAsync += (_, _) =>
                {
                    signal.TrySetResult();
                    return Task.CompletedTask;
                };
                return signal.Task;
            })
            .ToArray();
        try
        {
            await rabbit.StopAsync(token);
            await Task.WhenAll(closed).WaitAsync(token);
            Assert.False(commands.IsOpen);
            Assert.False(replies.IsOpen);
            Assert.False(intake.IsOpen);
        }
        finally
        {
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await rabbit.StartAsync(restore.Token);
        }

        // These are the original returned native channels, not replacements made by Gloaming.
        while (!commands.IsOpen || !replies.IsOpen || !intake.IsOpen)
            await Task.Delay(50, token);

        string second = Guid.NewGuid().ToString("D");
        string third = Guid.NewGuid().ToString("D");
        Assert.All(
            new[] { commands, replies, intake },
            channel => Assert.Equal(TimeSpan.FromSeconds(5), channel.ContinuationTimeout)
        );
        await PublishAsync(replies, queue, second, token);
        await PublishAsync(commands, queue, third, token);
        List<string?> delivered = [];
        for (int index = 0; index < 3; index++)
        {
            var delivery = await intake.BasicGetAsync(queue, false, token);
            Assert.NotNull(delivery);
            delivered.Add(delivery.BasicProperties.MessageId);
            Assert.Equal("original", Encoding.UTF8.GetString(delivery.Body.Span));
            await intake.BasicAckAsync(delivery.DeliveryTag, false, token);
        }

        Assert.Equal(new[] { first, second, third }.Order(), delivered.Order());
        await intake.QueueDeleteAsync(queue, cancellationToken: token);
        await session.DisposeAsync();
        Assert.All(new[] { commands, replies, intake }, channel => Assert.False(channel.IsOpen));
    }

    private static async Task PublishAsync(
        IChannel channel,
        string queue,
        string id,
        CancellationToken token
    ) =>
        await channel.BasicPublishAsync(
            "",
            queue,
            true,
            new BasicProperties { Persistent = true, MessageId = id },
            Encoding.UTF8.GetBytes("original"),
            token
        );
}
