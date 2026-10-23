using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gloaming.Samples.Messaging.Tests.Exports;

public sealed class OperationalVisibilityTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Credential = "outbox-visibility-operator-proof-0123456789";

    [Fact]
    public async Task OperatorStatusIsRouteScopedAndAuthorizedWhileHealthExposesOnlyStatus()
    {
        string connection = await DatabaseAsync();
        await QueueAsync(connection, "exports.render");
        await QueueAsync(connection, "other");
        await using var app = OutboxTestHost.Build(
            connection,
            "http://127.0.0.1:0",
            operatorToken: Credential
        );
        await app.StartAsync(Token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/operations/outbox/status", Token)).StatusCode
        );
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live", Token));
        Assert.Equal("Healthy", await client.GetStringAsync("/health/ready", Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Credential
        );
        var status = (
            await client.GetFromJsonAsync<OutboxBacklogSnapshot>(
                "/operations/outbox/status?routeKey=other",
                Token
            )
        )!;
        Assert.Equal(1, status.Pending);
        Assert.Equal(1, status.Eligible);
        Assert.Equal(0, status.DeadLetters);
        Assert.NotNull(status.OldestEligibleAge);
        string json = await client.GetStringAsync("/operations/outbox/status", Token);
        foreach (
            string excluded in new[]
            {
                "payload",
                "messageId",
                "tenantKey",
                "leaseToken",
                "traceParent",
                "exception",
            }
        )
            Assert.DoesNotContain(excluded, json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingStorageIsUnreadyAndUnknownWithoutDisclosingDetailsOrInventingZero(
        bool unreachable
    )
    {
        string connection = unreachable
            ? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1"
            : await postgres.CreateDatabaseAsync(Token);
        await using var app = OutboxTestHost.Build(
            connection,
            "http://127.0.0.1:0",
            operatorToken: Credential
        );
        var factory = app.Services.GetRequiredService<IMeterFactory>();
        int measurements = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, native) =>
            {
                if (
                    instrument.Meter.Name == "Gloaming.OutboxDemo.Outbox"
                    && ReferenceEquals(instrument.Meter.Scope, factory)
                )
                    native.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, _, _) => Interlocked.Increment(ref measurements)
        );
        listener.SetMeasurementEventCallback<double>(
            (_, _, _, _) => Interlocked.Increment(ref measurements)
        );
        listener.Start();
        await app.StartAsync(Token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live", Token));
        var ready = await client.GetAsync("/health/ready", Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync(Token));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/operations/outbox/status", Token)).StatusCode
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Credential
        );
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/operations/outbox/status", Token)).StatusCode
        );
        listener.RecordObservableInstruments();
        Assert.Equal(0, measurements);
    }

    [Fact]
    public async Task FaultedWorkerUnderNativeIgnoreIsUnreadyWhileTheHostRemainsLive()
    {
        string connection = await DatabaseAsync();
        await QueueAsync(connection, "exports.render");
        await using (var database = ExportDbContext.Create(connection))
            await database.Database.ExecuteSqlRawAsync(
                "ALTER TABLE exports.outgoing_work ADD CONSTRAINT completion_unavailable CHECK (dispatched_at IS NULL)",
                Token
            );
        await using var receiver = await HttpReceiver.StartAsync(Token);
        await using var app = OutboxTestHost.Build(
            connection,
            "http://127.0.0.1:0",
            receiver.Endpoint,
            worker: true,
            operatorToken: Credential
        );
        app
            .Services.GetRequiredService<IOptions<HostOptions>>()
            .Value.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        await app.StartAsync(Token);
        var worker = app
            .Services.GetServices<IHostedService>()
            .OfType<BackgroundService>()
            .Single();
        await Assert.ThrowsAsync<OutboxCompletionException>(() =>
            worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token)
        );
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live", Token));
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/health/ready", Token)).StatusCode
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Credential
        );
        var status = (
            await client.GetFromJsonAsync<OutboxBacklogSnapshot>("/operations/outbox/status", Token)
        )!;
        Assert.Equal(1, status.Leased);
        Assert.Single(receiver.Messages);
    }

    [Fact]
    public async Task IdleWorkerWithDelayedWorkIsReadyAndAStoppedWorkerIsUnready()
    {
        string connection = await DatabaseAsync();
        await QueueAsync(connection, "exports.render");
        await using (var database = ExportDbContext.Create(connection))
            await database.Database.ExecuteSqlRawAsync(
                "UPDATE exports.outgoing_work SET available_at = clock_timestamp() + interval '1 hour'",
                Token
            );
        await using var receiver = await HttpReceiver.StartAsync(Token);
        await using var app = OutboxTestHost.Build(
            connection,
            "http://127.0.0.1:0",
            receiver.Endpoint,
            worker: true
        );
        await app.StartAsync(Token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal("Healthy", await client.GetStringAsync("/health/ready", Token));
        var worker = app
            .Services.GetServices<IHostedService>()
            .OfType<BackgroundService>()
            .Single();
        await worker.StopAsync(Token);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/health/ready", Token)).StatusCode
        );
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live", Token));
        Assert.Empty(receiver.Messages);
    }

    [Fact]
    public async Task NativeGaugesKeepTheirLastObservationWithExplicitAgeAndRecoverAfterStorageRepair()
    {
        string connection = await DatabaseAsync();
        await QueueAsync(connection, "exports.render");
        await using var app = OutboxTestHost.Build(
            connection,
            "http://127.0.0.1:0",
            observationInterval: TimeSpan.FromMilliseconds(50)
        );
        var diagnostics = new ObservationLog();
        app.Services.GetRequiredService<ILoggerFactory>().AddProvider(diagnostics);
        var factory = app.Services.GetRequiredService<IMeterFactory>();
        var measurements = new ConcurrentDictionary<string, double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, native) =>
            {
                if (
                    instrument.Meter.Name == "Gloaming.OutboxDemo.Outbox"
                    && ReferenceEquals(instrument.Meter.Scope, factory)
                )
                    native.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
            {
                Assert.Equal(0, tags.Length);
                measurements[instrument.Name] = value;
            }
        );
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                Assert.Equal(0, tags.Length);
                measurements[instrument.Name] = value;
            }
        );
        listener.Start();
        await app.StartAsync(Token);
        await UntilAsync(() =>
        {
            listener.RecordObservableInstruments();
            return measurements.GetValueOrDefault("exports.outbox.pending") == 1;
        });
        Assert.Equal(1, measurements["exports.outbox.eligible"]);
        Assert.Equal(0, measurements["exports.outbox.dead_letters"]);
        Assert.True(measurements.ContainsKey("exports.outbox.oldest_eligible.age"));
        await using (var locked = ExportDbContext.Create(connection))
        {
            await using var transaction = await locked.Database.BeginTransactionAsync(Token);
            await locked.Database.ExecuteSqlRawAsync(
                "LOCK TABLE exports.outgoing_work IN ACCESS EXCLUSIVE MODE",
                Token
            );
            await Task.Run(listener.RecordObservableInstruments, Token)
                .WaitAsync(TimeSpan.FromSeconds(5), Token);
        }

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await using (var database = ExportDbContext.Create(connection))
            await database.Database.ExecuteSqlRawAsync(
                "ALTER TABLE exports.outgoing_work RENAME TO held_work",
                Token
            );
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/health/ready", Token)).StatusCode
        );
        await diagnostics.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        listener.RecordObservableInstruments();
        double age = measurements["exports.outbox.observation.age"];
        await UntilAsync(() =>
        {
            listener.RecordObservableInstruments();
            return measurements["exports.outbox.observation.age"] > age;
        });
        Assert.Equal(1, measurements["exports.outbox.pending"]);
        await using (var database = ExportDbContext.Create(connection))
            await database.Database.ExecuteSqlRawAsync(
                "ALTER TABLE exports.held_work RENAME TO outgoing_work",
                Token
            );
        await QueueAsync(connection, "exports.render");
        await UntilAsync(() =>
        {
            listener.RecordObservableInstruments();
            return measurements["exports.outbox.pending"] == 2;
        });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", Token)).StatusCode);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!condition())
            await Task.Delay(20, deadline.Token);
    }

    private sealed class ObservationLog : ILoggerProvider, ILogger
    {
        internal TaskCompletionSource Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (
                logLevel == LogLevel.Warning
                && formatter(state, exception)
                    .StartsWith("Export outbox observation failed", StringComparison.Ordinal)
            )
                Failed.TrySetResult();
        }

        public void Dispose() { }
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = ExportDbContext.Create(connection);
        await database.Database.MigrateAsync(Token);
        return connection;
    }

    private static async Task QueueAsync(string connection, string route)
    {
        await using var database = ExportDbContext.Create(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        new EfOutbox<ExportDbContext>(database).Enqueue(
            new(
                Guid.NewGuid(),
                route,
                "exports.render",
                1,
                System.Text.Json.JsonSerializer.SerializeToElement(new { Pages = 1 })
            )
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }
}
