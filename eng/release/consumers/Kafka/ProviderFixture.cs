using System.Text.Json;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;

sealed class ProviderFixture(
    string postgresImage,
    string kafkaImage,
    string password,
    string run,
    string cleanupPath,
    int proxyPort
) : IAsyncDisposable
{
    private const int KafkaPort = 9092;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(postgresImage)
        .WithName($"gloaming-kafka-proof-pg-{run}")
        .WithDatabase("kafka_proof")
        .WithPassword(password)
        // Keep PostgreSQL's normal durability settings in this recovery proof.
        .WithCommand(new OverwriteEnumerable<string>(["postgres"]))
        .WithCreateParameterModifier(parameters => BindLoopback(parameters, 5432))
        .Build();
    private readonly IContainer _broker = new ContainerBuilder(kafkaImage)
        .WithName($"gloaming-kafka-proof-broker-{run}")
        .WithPortBinding(KafkaPort, assignRandomHostPort: true)
        .WithCreateParameterModifier(parameters => BindLoopback(parameters, KafkaPort))
        .WithEnvironment("KAFKA_NODE_ID", "1")
        .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
        .WithEnvironment(
            "KAFKA_LISTENERS",
            "INTERNAL://:19092,CONTROLLER://:19093,EXTERNAL://:9092"
        )
        .WithEnvironment(
            "KAFKA_ADVERTISED_LISTENERS",
            FormattableString.Invariant(
                $"INTERNAL://localhost:19092,EXTERNAL://127.0.0.1:{proxyPort}"
            )
        )
        .WithEnvironment(
            "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
            "INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT"
        )
        .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
        .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
        .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:19093")
        .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
        .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
        .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
        .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
        .WithWaitStrategy(
            Wait.ForUnixContainer()
                .UntilCommandIsCompleted(
                    "/opt/kafka/bin/kafka-topics.sh",
                    "--bootstrap-server",
                    "localhost:19092",
                    "--list"
                )
        )
        .Build();

    internal string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Pooling = false,
        }.ConnectionString;

    internal string BrokerAddress =>
        FormattableString.Invariant($"127.0.0.1:{_broker.GetMappedPublicPort(KafkaPort)}");

    internal bool Completed { get; set; }

    internal async Task StartAsync(CancellationToken cancellation)
    {
        await _postgres.StartAsync(cancellation);
        Console.WriteLine("PostgreSQL fixture ready.");
        await _broker.StartAsync(cancellation);
        Console.WriteLine("Kafka fixture ready.");
    }

    internal async Task RestartBrokerAsync(CancellationToken cancellation)
    {
        await _broker.StopAsync(cancellation);
        await _broker.StartAsync(cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        List<CleanupResult> results = [];
        List<Exception> failures = [];
        foreach (
            var (name, container) in new (string, IContainer)[]
            {
                ("broker", _broker),
                ("pg", _postgres),
            }
        )
        {
            string? id = container.CreatedTime == default ? null : container.Id;
            if (!Completed && id is not null)
                await WriteRecentLogsAsync(name, container);

            try
            {
                await container.DisposeAsync();
                results.Add(new(name, id, id is null ? "not-created" : "removed", null));
            }
            catch (Exception error)
            {
                failures.Add(error);
                results.Add(
                    new(
                        name,
                        id,
                        "failed",
                        error.Message.Replace(password, "[redacted]", StringComparison.Ordinal)
                    )
                );
            }
        }

        await File.WriteAllTextAsync(cleanupPath, JsonSerializer.Serialize(results, JsonOptions));
        if (failures.Count > 0)
            throw new AggregateException("Provider fixture cleanup failed.", failures);
    }

    private static void BindLoopback(CreateContainerParameters parameters, int port)
    {
        string key = FormattableString.Invariant($"{port}/tcp");
        if (
            parameters.HostConfig?.PortBindings is not { } ports
            || !ports.TryGetValue(key, out var bindings)
            || bindings is null
            || bindings.Count != 1
        )
            throw new InvalidOperationException($"Expected one published binding for {key}.");

        bindings[0].HostIP = "127.0.0.1";
    }

    private static async Task WriteRecentLogsAsync(string name, IContainer container)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var (stdout, stderr) = await container.GetLogsAsync(ct: deadline.Token);
            Console.Error.WriteLine($"--- {name} ---");
            Console.Error.WriteLine(stdout[^Math.Min(16_384, stdout.Length)..]);
            Console.Error.WriteLine(stderr[^Math.Min(16_384, stderr.Length)..]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Could not collect {name} logs: {error.Message}");
        }
    }

    private sealed record CleanupResult(string Name, string? Id, string Outcome, string? Error);
}
