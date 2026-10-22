using System.Text;
using Gloaming.Messaging.RabbitMQ;
using ModulithFoundry.Tests.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace ModulithFoundry.RabbitMqTests;

public sealed class LifecycleTests(RabbitMqFixture rabbit) : IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task FailedNativeSetupPreservesItsErrorAndClosesEarlierAcquiredChannels()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        IChannel? acquired = null;
        IChannel? failed = null;
        var failure = await Assert.ThrowsAsync<OperationInterruptedException>(async () =>
        {
            await using var session = await OpenAsync(token);
            acquired = await session.CreateChannelAsync(cancellationToken: token);
            failed = await session.CreateChannelAsync(cancellationToken: token);
            await failed.QueueDeclarePassiveAsync("missing-" + Guid.NewGuid().ToString("N"), token);
        });
        Assert.NotNull(failure.ShutdownReason);
        Assert.Equal(404, failure.ShutdownReason.ReplyCode);
        Assert.NotNull(acquired);
        Assert.NotNull(failed);
        Assert.False(acquired.IsOpen);
        Assert.False(failed.IsOpen);
    }

    [Fact]
    public async Task ObserverIncludesLaterChannelsAndEarlierFailureSurvivesIntentionalStop()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stopping = new CancellationTokenSource();
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        var signal = new TaskCompletionSource<ShutdownEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int notifications = 0;
        using var observer = session.ObserveTerminalShutdown(
            stopping.Token,
            reason =>
            {
                Interlocked.Increment(ref notifications);
                stopping.Cancel();
                signal.TrySetResult(reason);
            }
        );
        var later = await session.CreateChannelAsync(cancellationToken: token);
        var failure = await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            later.QueueDeclarePassiveAsync("missing-" + Guid.NewGuid().ToString("N"), token)
        );
        Assert.Same(failure.ShutdownReason, await signal.Task.WaitAsync(token));
        await session.DisposeAsync();
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledLifetimeOrDisposedRegistrationSuppressesNativeClose(
        bool cancelLifetime
    )
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stopping = new CancellationTokenSource();
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        int notifications = 0;
        using var observer = session.ObserveTerminalShutdown(
            stopping.Token,
            _ => Interlocked.Increment(ref notifications)
        );
        if (cancelLifetime)
            stopping.Cancel();
        else
        {
            observer.Dispose();
            Assert.Throws<InvalidOperationException>(() =>
                session.ObserveTerminalShutdown(CancellationToken.None, _ => { })
            );
        }

        // A channel created after observer disposal must not reattach an active observer.
        var channel = await session.CreateChannelAsync(cancellationToken: token);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            channel.QueueDeclarePassiveAsync("missing-" + Guid.NewGuid().ToString("N"), token)
        );
        await closed.Task.WaitAsync(token);
        await session.DisposeAsync();
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task SessionDisposalIsIntentionalAndObserverCannotBeRegisteredAgain()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        int notifications = 0;
        using var observer = session.ObserveTerminalShutdown(
            CancellationToken.None,
            _ => Interlocked.Increment(ref notifications)
        );
        await session.CreateChannelAsync(cancellationToken: token);
        Assert.Throws<InvalidOperationException>(() =>
            session.ObserveTerminalShutdown(CancellationToken.None, _ => { })
        );
        await session.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() =>
            session.ObserveTerminalShutdown(CancellationToken.None, _ => { })
        );
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task ConcurrentCreationAndRepeatedDisposalLeaveNoUsableOwnedChannels()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        var first = await session.CreateChannelAsync(cancellationToken: token);
        var pending = Enumerable
            .Range(0, 16)
            .Select(_ => session.CreateChannelAsync(cancellationToken: token))
            .ToArray();
        var closers = Enumerable.Range(0, 8).Select(_ => session.DisposeAsync().AsTask()).ToArray();
        List<IChannel> acquired = [first];
        foreach (var creation in pending)
        {
            try
            {
                acquired.Add(await creation);
            }
            catch (ObjectDisposedException) { }
        }

        await Task.WhenAll(closers).WaitAsync(token);
        Assert.All(acquired, channel => Assert.False(channel.IsOpen));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.CreateChannelAsync(cancellationToken: token)
        );
    }

    [Fact]
    public async Task DisposalRejectsNewChannelsWhileNativeShutdownIsStillInFlight()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        var channel = await session.CreateChannelAsync(cancellationToken: token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task disposal = session.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(token);
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                session.CreateChannelAsync(cancellationToken: token)
            );
        }
        finally
        {
            release.TrySetResult();
            await disposal.WaitAsync(token);
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.DisposeAsync().AsTask()));
        Assert.False(channel.IsOpen);
    }

    [Fact]
    public async Task LateObserverReceivesOriginalChannelFailureOnceWhileAnotherChannelSurvives()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        var failed = await session.CreateChannelAsync(cancellationToken: token);
        var surviving = await session.CreateChannelAsync(cancellationToken: token);
        var failure = await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            failed.QueueDeclarePassiveAsync("missing-" + Guid.NewGuid().ToString("N"), token)
        );
        Assert.NotNull(failure.ShutdownReason);
        Assert.True(surviving.IsOpen);
        var signal = new TaskCompletionSource<ShutdownEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int notifications = 0;
        using var observation = session.ObserveTerminalShutdown(
            CancellationToken.None,
            reason =>
            {
                Interlocked.Increment(ref notifications);
                signal.TrySetResult(reason);
            }
        );
        Assert.Same(failure.ShutdownReason, await signal.Task.WaitAsync(token));
        Assert.Equal(404, failure.ShutdownReason.ReplyCode);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        surviving.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            surviving.QueueDeclarePassiveAsync(
                "also-missing-" + Guid.NewGuid().ToString("N"),
                token
            )
        );
        await closed.Task.WaitAsync(token);
        await session.DisposeAsync();
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task NativeChannelsConfirmPublishAndDisposalRequeuesUnacknowledgedDelivery()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var session = await OpenAsync(token);
        var publisher = await session.CreateChannelAsync(new(true, true), token);
        var intake = await session.CreateChannelAsync(cancellationToken: token);
        var queue = await intake.QueueDeclareAsync(
            "session-proof-" + Guid.NewGuid().ToString("N"),
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: token
        );
        string id = Guid.NewGuid().ToString("D");
        await publisher.BasicPublishAsync(
            "",
            queue.QueueName,
            true,
            new BasicProperties { Persistent = true, MessageId = id },
            Encoding.UTF8.GetBytes("retained"),
            token
        );
        var first = await intake.BasicGetAsync(queue.QueueName, false, token);
        Assert.NotNull(first);
        Assert.Equal(id, first.BasicProperties.MessageId);
        Assert.Equal(TimeSpan.FromSeconds(5), publisher.ContinuationTimeout);
        await session.DisposeAsync();
        Assert.False(publisher.IsOpen);
        Assert.False(intake.IsOpen);

        await using var replacement = await OpenAsync(token);
        var recovered = await replacement.CreateChannelAsync(cancellationToken: token);
        var redelivery = await recovered.BasicGetAsync(queue.QueueName, false, token);
        Assert.NotNull(redelivery);
        Assert.True(redelivery.Redelivered);
        Assert.Equal(id, redelivery.BasicProperties.MessageId);
        Assert.Equal("retained", Encoding.UTF8.GetString(redelivery.Body.Span));
        await recovered.BasicAckAsync(redelivery.DeliveryTag, false, token);
        await recovered.QueueDeleteAsync(queue.QueueName, cancellationToken: token);
    }

    private Task<RabbitMqSession> OpenAsync(CancellationToken token) =>
        RabbitMqSession.OpenAsync(
            factory =>
            {
                factory.Uri = new Uri(rabbit.ConnectionString);
                factory.AutomaticRecoveryEnabled = false;
                factory.ContinuationTimeout = TimeSpan.FromSeconds(45); // Session options explicitly win.
            },
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            token
        );
}
