using System.Diagnostics;
using System.Diagnostics.Metrics;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModulithFoundry.Samples.Messaging.Exports;

namespace ModulithFoundry.Samples.Messaging.Tests.Fixtures.Outbox;

// Consumer-owned sampling cadence and instruments. Observable callbacks only read cached data.
internal sealed partial class ExportOutboxMetrics : IHostedService, IAsyncDisposable
{
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<ExportOutboxMetrics> logger;
    private readonly TimeSpan interval;
    private readonly CancellationTokenSource stopping = new();
    private Observation? observation;
    private Task? sampling;
    private int disposed;

    internal ExportOutboxMetrics(
        IServiceScopeFactory scopes,
        IMeterFactory meters,
        ILogger<ExportOutboxMetrics> logger,
        TimeSpan interval
    )
    {
        if (interval <= TimeSpan.Zero || interval.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(interval));

        this.scopes = scopes;
        this.logger = logger;
        this.interval = interval;
        var meter = meters.Create("ModulithFoundry.OutboxDemo.Outbox");
        meter.CreateObservableGauge(
            "exports.outbox.pending",
            () => Observe(s => s.Pending),
            "{message}"
        );
        meter.CreateObservableGauge(
            "exports.outbox.eligible",
            () => Observe(s => s.Eligible),
            "{message}"
        );
        meter.CreateObservableGauge(
            "exports.outbox.delayed",
            () => Observe(s => s.Delayed),
            "{message}"
        );
        meter.CreateObservableGauge(
            "exports.outbox.leased",
            () => Observe(s => s.Leased),
            "{message}"
        );
        meter.CreateObservableGauge(
            "exports.outbox.dead_letters",
            () => Observe(s => s.DeadLetters),
            "{message}"
        );
        meter.CreateObservableGauge("exports.outbox.oldest_eligible.age", ObserveOldestAge, "s");
        meter.CreateObservableGauge("exports.outbox.observation.age", ObserveSampleAge, "s");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        sampling = Task.Run(() => SampleAsync(stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await stopping.CancelAsync();
        if (sampling is not null)
            await sampling.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        await StopAsync(CancellationToken.None);
        stopping.Dispose();
    }

    private async Task SampleAsync(CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    long started = Stopwatch.GetTimestamp();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellation
                    );
                    deadline.CancelAfter(TimeSpan.FromSeconds(3));
                    OutboxBacklogSnapshot snapshot;
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        snapshot = await scope
                            .ServiceProvider.GetRequiredService<ExportDbContext>()
                            .ReadPostgresOutboxBacklogAsync("exports.render", deadline.Token);
                    }

                    Volatile.Write(ref observation, new(snapshot, started));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception failure)
                {
                    ObservationFailed(logger, failure);
                }

                await Task.Delay(interval, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private IEnumerable<Measurement<long>> Observe(Func<OutboxBacklogSnapshot, long> select)
    {
        var current = Volatile.Read(ref observation);
        if (current is not null)
            yield return new(select(current.Snapshot));
    }

    private IEnumerable<Measurement<double>> ObserveOldestAge()
    {
        var current = Volatile.Read(ref observation);
        if (current?.Snapshot.OldestEligibleAge is { } age)
            yield return new(age.TotalSeconds);
    }

    private IEnumerable<Measurement<double>> ObserveSampleAge()
    {
        var current = Volatile.Read(ref observation);
        if (current is not null)
            yield return new(Stopwatch.GetElapsedTime(current.Timestamp).TotalSeconds);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Export outbox observation failed; any prior metric sample is aging."
    )]
    private static partial void ObservationFailed(ILogger logger, Exception exception);

    private sealed record Observation(OutboxBacklogSnapshot Snapshot, long Timestamp);
}
