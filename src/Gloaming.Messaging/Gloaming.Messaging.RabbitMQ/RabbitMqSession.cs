using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.RabbitMQ;

/// <summary>Owns a native connection and its channels without owning transport or host policy.</summary>
public sealed partial class RabbitMqSession : IAsyncDisposable
{
    private readonly object gate = new();

    // Only WaitAsync is used; no wait handle is allocated. Keep it alive for admitted waiters.
    private readonly SemaphoreSlim acquisition = new(1, 1);
    private readonly IConnection connection;
    private readonly RabbitMqSessionOptions options;
    private readonly bool automaticRecovery;
    private readonly List<IChannel> channels = [];
    private Task? disposal;
    private ShutdownEventArgs? firstShutdown;
    private Observation? observation;
    private bool registered;
    private bool disposing;
    private bool detached;

    private RabbitMqSession(
        IConnection connection,
        RabbitMqSessionOptions options,
        bool automaticRecovery
    )
    {
        (this.connection, this.options, this.automaticRecovery) = (
            connection,
            options,
            automaticRecovery
        );
        if (!automaticRecovery)
        {
            connection.ConnectionShutdownAsync += OnShutdownAsync;
            connection.ConnectionBlockedAsync += OnBlockedAsync;
            connection.ConnectionUnblockedAsync += OnUnblockedAsync;
        }
    }

    /// <summary>Configures a fresh native factory, applies the protocol timeout, and opens a connection.</summary>
    public static async Task<RabbitMqSession> OpenAsync(
        Action<ConnectionFactory> configure,
        RabbitMqSessionOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(options);
        ValidateTimeout(options.ProtocolTimeout, nameof(options.ProtocolTimeout));
        ValidateTimeout(options.ConnectionCloseTimeout, nameof(options.ConnectionCloseTimeout));
        cancellationToken.ThrowIfCancellationRequested();

        var factory = new ConnectionFactory();
        configure(factory);
        factory.ContinuationTimeout = options.ProtocolTimeout;
        return new(
            await factory.CreateConnectionAsync(cancellationToken),
            options,
            factory.AutomaticRecoveryEnabled
        );
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameter)
    {
        // Native continuations/abort use .NET timers: reject infinite and unsupported windows.
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(
                parameter,
                "Use a positive finite native timer window."
            );
    }

    /// <summary>Creates an owned native channel. Callers retain native publishing and intake APIs.</summary>
    public async Task<IChannel> CreateChannelAsync(
        CreateChannelOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        lock (gate)
            ObjectDisposedException.ThrowIf(disposing, this);

        await acquisition.WaitAsync(cancellationToken);
        try
        {
            lock (gate)
                ObjectDisposedException.ThrowIf(disposing, this);

            var channel = await connection.CreateChannelAsync(options, cancellationToken);
            bool observe;
            lock (gate)
            {
                channels.Add(channel);
                observe = !automaticRecovery && !detached;
            }

            if (observe)
            {
                // Late native subscriptions can invoke immediately: never subscribe under gate.
                channel.ChannelShutdownAsync += OnShutdownAsync;
                lock (gate)
                    observe = !detached;

                if (!observe)
                    channel.ChannelShutdownAsync -= OnShutdownAsync;
            }

            return channel;
        }
        finally
        {
            acquisition.Release();
        }
    }

    /// <summary>Observes terminal loss on a nonrecovering session; the callback only signals consumer stop.</summary>
    [SuppressMessage(
        "Design",
        "CA1068:CancellationToken parameters must come last",
        Justification = "This token identifies intentional session shutdown, not cancellation of registration."
    )]
    public IDisposable ObserveTerminalShutdown(
        CancellationToken intentionalStop,
        Action<ShutdownEventArgs> onUnexpectedShutdown,
        ILogger? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(onUnexpectedShutdown);

        Observation current;
        Observation? admitted;
        ShutdownEventArgs? reason;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposing, this);
            if (automaticRecovery)
                throw new InvalidOperationException(
                    "Disable native automatic recovery before observing terminal shutdown."
                );
            if (registered)
                throw new InvalidOperationException(
                    "A session permits one terminal observer registration."
                );

            registered = true;
            current = new(this, onUnexpectedShutdown, logger, intentionalStop);
            observation = current;
            admitted = AdmitLocked();
            reason = firstShutdown;
        }

        admitted?.Invoke(reason!);
        return current;
    }

    /// <summary>Aborts the connection then disposes all acquired resources.</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposing = true;
            observation = null;
            DetachNativeHandlersLocked();
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private Task OnShutdownAsync(object sender, ShutdownEventArgs reason)
    {
        Observation? admitted;
        ShutdownEventArgs first;
        lock (gate)
        {
            if (disposing || detached)
                return Task.CompletedTask;

            firstShutdown ??= reason;
            first = firstShutdown;
            admitted = AdmitLocked();
        }

        // Never run user code under the state lock or await teardown in a native callback.
        admitted?.Invoke(first);
        return Task.CompletedTask;
    }

    private Observation? AdmitLocked()
    {
        if (
            firstShutdown is null
            || observation is null
            || observation.Delivered
            || observation.IntentionalStop.IsCancellationRequested
        )
            return null;

        observation.Delivered = true;
        return observation;
    }

    private Task OnBlockedAsync(object sender, ConnectionBlockedEventArgs reason)
    {
        var logger = DiagnosticLogger();
        if (logger is not null)
            BrokerBlocked(logger, reason.Reason);

        return Task.CompletedTask;
    }

    private Task OnUnblockedAsync(object sender, AsyncEventArgs args)
    {
        var logger = DiagnosticLogger();
        if (logger is not null)
            BrokerUnblocked(logger);

        return Task.CompletedTask;
    }

    private ILogger? DiagnosticLogger()
    {
        lock (gate)
            return !disposing && observation is { IntentionalStop.IsCancellationRequested: false }
                ? observation.Logger
                : null;
    }

    private void Detach(Observation current)
    {
        lock (gate)
        {
            if (!ReferenceEquals(observation, current))
                return;

            observation = null;
            DetachNativeHandlersLocked();
        }
    }

    private void DetachNativeHandlersLocked()
    {
        if (detached || automaticRecovery)
            return;

        detached = true;
        connection.ConnectionShutdownAsync -= OnShutdownAsync;
        connection.ConnectionBlockedAsync -= OnBlockedAsync;
        connection.ConnectionUnblockedAsync -= OnUnblockedAsync;
        foreach (var channel in channels)
            channel.ChannelShutdownAsync -= OnShutdownAsync;
    }

    private sealed class Observation(
        RabbitMqSession owner,
        Action<ShutdownEventArgs> callback,
        ILogger? logger,
        CancellationToken intentionalStop
    ) : IDisposable
    {
        internal CancellationToken IntentionalStop => intentionalStop;
        internal ILogger? Logger => logger;
        internal bool Delivered { get; set; }

        internal void Invoke(ShutdownEventArgs reason)
        {
            if (logger is not null)
                BrokerLost(logger, reason.Initiator, reason.ReplyCode);

            callback(reason);
        }

        public void Dispose() => owner.Detach(this);
    }

    private async Task DisposeCoreAsync()
    {
        // DisposeAsync admits cleanup under gate; native callbacks must run outside it.
        await Task.Yield();
        await acquisition.WaitAsync();
        try
        {
            await DisposeResourcesAsync();
        }
        finally
        {
            acquisition.Release();
        }
    }

    private async Task DisposeResourcesAsync()
    {
        List<Exception> failures = [];
        try
        {
            await connection.AbortAsync(options.ConnectionCloseTimeout);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        for (int index = channels.Count - 1; index >= 0; index--)
        {
            try
            {
                await channels[index].DisposeAsync();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        if (failures.Count > 1)
            throw new AggregateException(failures);
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Broker session lost ({Initiator}, code {ReplyCode}). Consumer stop/restart policy applies."
    )]
    private static partial void BrokerLost(
        ILogger logger,
        ShutdownInitiator initiator,
        ushort replyCode
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Broker connection blocked: {Reason}. Publication acceptance remains unknown."
    )]
    private static partial void BrokerBlocked(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Broker connection unblocked.")]
    private static partial void BrokerUnblocked(ILogger logger);
}
