using System.Net;
using System.Net.Http.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.InboxDemo;
using ModulithFoundry.Samples.MessagingProducerDemo;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using RabbitMQ.Client;

namespace ModulithFoundry.Samples.MessagingWorkerDemo.Tests;

internal sealed class WorkerTopology(
    string exports,
    string rendering,
    string broker,
    Dictionary<string, string>? telemetry = null
) : IAsyncDisposable
{
    private readonly List<ChildHost> children = [];
    private readonly string queue = "worker-proof-" + Guid.NewGuid().ToString("N");

    internal string Exports => exports;
    internal string Rendering => rendering;

    internal static async Task<WorkerTopology> CreateAsync(
        PostgreSqlFixture postgres,
        RabbitMqFixture rabbit,
        CancellationToken cancellation,
        Dictionary<string, string>? telemetry = null
    ) =>
        new(
            await postgres.CreateDatabaseAsync(cancellation),
            await postgres.CreateDatabaseAsync(cancellation),
            rabbit.ConnectionString,
            telemetry
        );

    internal ChildHost Worker(string role, string name, int leaseSeconds = 30)
    {
        var environment = telemetry is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new(telemetry, StringComparer.Ordinal);
        environment["OTEL_SERVICE_NAME"] = name;
        if (role is "setup" or "dispatch")
            environment["ConnectionStrings__Exports"] = WithName(exports, name);
        if (role is "setup" or "receive" or "process")
            environment["ConnectionStrings__Rendering"] = WithName(rendering, name);
        if (role is "setup" or "dispatch" or "receive")
        {
            environment["RabbitMQ__Uri"] = broker;
            environment["RabbitMQ__Queue"] = queue;
        }

        environment["Worker__LeaseSeconds"] = leaseSeconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
        return Track(new(typeof(WorkerAssemblyMarker).Assembly, environment, "--role", role));
    }

    internal async Task SetupAsync(CancellationToken cancellation)
    {
        var setup = Worker("setup", "setup");
        Assert.True(await setup.WaitForExitAsync(cancellation) == 0, setup.Output);
    }

    internal async Task<ChildHost> StartWorkerAsync(
        string role,
        string name,
        CancellationToken cancellation,
        int leaseSeconds = 30
    )
    {
        var worker = Worker(role, name, leaseSeconds);
        await worker.WaitForOutputAsync("Application started.", cancellation);
        return worker;
    }

    internal async Task<(ChildHost Host, HttpClient Client)> StartApiAsync(
        CancellationToken cancellation
    )
    {
        var api = Track(
            new(
                typeof(ProducerAssemblyMarker).Assembly,
                new(telemetry ?? [], StringComparer.Ordinal)
                {
                    ["OTEL_SERVICE_NAME"] = "proof-api",
                    ["ConnectionStrings__Exports"] = exports,
                    ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
                }
            )
        );
        string listening = await api.WaitForOutputAsync("Now listening on:", cancellation);
        await api.WaitForOutputAsync("Application started.", cancellation);
        var endpoint = new Uri(
            listening[(listening.IndexOf("http://", StringComparison.Ordinal))..].Trim()
        );
        var client = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync("/health/live", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (api, client);
    }

    internal static async Task<Guid> SubmitAsync(
        HttpClient client,
        int pages,
        CancellationToken cancellation
    )
    {
        Guid id = Guid.NewGuid();
        using var draft = await client.PostAsJsonAsync(
            "/exports",
            new { Id = id, Pages = pages },
            cancellation
        );
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        using var submission = await client.PostAsJsonAsync(
            $"/exports/{id}/submit",
            new { ExpectedVersion = 1 },
            cancellation
        );
        Assert.Equal(HttpStatusCode.Accepted, submission.StatusCode);
        return id;
    }

    internal async Task<Guid> MessageIdAsync(CancellationToken cancellation)
    {
        await using var database = ExportDbContext.Create(exports);
        return await database
            .Set<OutboxMessageRecord>()
            .Select(row => row.MessageId)
            .SingleAsync(cancellation);
    }

    internal async Task WaitForJobsAsync(int count, CancellationToken cancellation) =>
        await EventuallyAsync(
            async () =>
            {
                await using var database = RenderDbContext.Create(rendering);
                return await database.Set<RenderJob>().CountAsync(cancellation) == count
                    && await database
                        .Set<InboxMessageRecord>()
                        .CountAsync(row => row.ProcessedAt != null, cancellation) == count;
            },
            cancellation
        );

    internal async Task AssertCountsAsync(
        int retained,
        int completed,
        int jobs,
        CancellationToken cancellation
    )
    {
        await using var database = RenderDbContext.Create(rendering);
        Assert.Equal(retained, await database.Set<InboxMessageRecord>().CountAsync(cancellation));
        Assert.Equal(
            completed,
            await database
                .Set<InboxMessageRecord>()
                .CountAsync(row => row.ProcessedAt != null, cancellation)
        );
        Assert.Equal(jobs, await database.Set<RenderJob>().CountAsync(cancellation));
    }

    internal async Task AssertJobAsync(Guid exportId, int pages, CancellationToken cancellation)
    {
        await using var database = RenderDbContext.Create(rendering);
        var job = await database
            .Set<RenderJob>()
            .SingleAsync(row => row.ExportRequestId == exportId, cancellation);
        Assert.Equal(pages, job.Pages);
    }

    internal async Task AssertQueueEmptyAsync(CancellationToken cancellation)
    {
        await using var connection = await new ConnectionFactory
        {
            Uri = new Uri(broker),
        }.CreateConnectionAsync(cancellation);
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: cancellation
        );
        Assert.Null(await channel.BasicGetAsync(queue, autoAck: false, cancellation));
    }

    // The fixture is a trusted operator. HTTP credential/authorization proofs live in InboxDemo.Tests.
    internal async Task RetryRenderingAsync(CancellationToken cancellation)
    {
        await using var read = RenderDbContext.Create(rendering);
        var stopped = await read.Set<InboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.QuarantinedAt != null, cancellation);
        var services = new ServiceCollection();
        services.AddDbContext<RenderDbContext>(options =>
            RenderDbContext.Configure(options, rendering)
        );
        services.AddPostgresInboxRecovery<RenderDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(
            InboxRetryResult.Retried,
            await scope
                .ServiceProvider.GetRequiredService<IInboxRecovery<RenderDbContext>>()
                .RetryAsync(
                    stopped.SubscriptionKey,
                    stopped.ProducerKey,
                    stopped.MessageId,
                    stopped.Revision,
                    cancellation
                )
        );
    }

    internal static async Task EventuallyAsync(
        Func<Task<bool>> condition,
        CancellationToken cancellation
    )
    {
        while (!await condition())
            await Task.Delay(50, cancellation);
    }

    internal static async Task<long> SqlCountAsync(
        string connectionString,
        string sql,
        CancellationToken cancellation
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellation),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    internal static Task WaitForBackendExitAsync(
        string connection,
        string applicationName,
        CancellationToken cancellation
    ) =>
        EventuallyAsync(
            async () =>
                await SqlCountAsync(
                    connection,
                    $"SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '{applicationName}'",
                    cancellation
                ) == 0,
            cancellation
        );

    public async ValueTask DisposeAsync()
    {
        foreach (var child in children.AsEnumerable().Reverse())
            await child.DisposeAsync();

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await new ConnectionFactory
        {
            Uri = new Uri(broker),
        }.CreateConnectionAsync(cleanup.Token);
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: cleanup.Token
        );
        // Only this proof's private queue; deleting a missing queue is harmless after failed setup.
        await channel.QueueDeleteAsync(queue, cancellationToken: cleanup.Token);
    }

    private ChildHost Track(ChildHost child)
    {
        children.Add(child);
        return child;
    }

    private static string WithName(string connection, string name) =>
        new NpgsqlConnectionStringBuilder(connection) { ApplicationName = name }.ConnectionString;
}
