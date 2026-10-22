using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Messaging.Exports;
using ModulithFoundry.Samples.Messaging.Tests.Fixtures.Outbox;

if (args.Contains("--cleanup", StringComparer.Ordinal))
{
    if (args.Length != 1)
        throw new ArgumentException("Use --cleanup as a separate finite maintenance role.");

    string maintenance =
        Environment.GetEnvironmentVariable("OUTBOX_DEMO_MAINTENANCE_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "Set OUTBOX_DEMO_MAINTENANCE_CONNECTION_STRING for cleanup."
        );
    string? cutoff = Environment.GetEnvironmentVariable("OUTBOX_DEMO_COMPLETED_BEFORE");
    if (
        !DateTimeOffset.TryParseExact(
            cutoff,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var completedBefore
        )
    )
        throw new ArgumentException(
            "Set OUTBOX_DEMO_COMPLETED_BEFORE to an explicit UTC timestamp ending in Z."
        );

    int batchSize = int.Parse(
        Environment.GetEnvironmentVariable("OUTBOX_DEMO_CLEANUP_BATCH_SIZE")
            ?? throw new InvalidOperationException(
                "Set OUTBOX_DEMO_CLEANUP_BATCH_SIZE from 1 through 1000."
            ),
        CultureInfo.InvariantCulture
    );
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await ExportOutboxRetention.RunAsync(
        maintenance,
        completedBefore,
        batchSize,
        Console.Out,
        deadline.Token
    );
    return;
}

string connection =
    Environment.GetEnvironmentVariable("OUTBOX_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set OUTBOX_DEMO_CONNECTION_STRING to a disposable PostgreSQL database."
    );
if (args.Contains("--setup", StringComparer.Ordinal))
{
    await using var database = ExportDbContext.Create(connection);
    await database.Database.MigrateAsync();
    Console.WriteLine("Exports schema migrated.");
    return;
}

bool worker = args.Contains("--worker", StringComparer.Ordinal);
if (args.Contains("--serve", StringComparer.Ordinal))
{
    string? receiverAddress = Environment.GetEnvironmentVariable("OUTBOX_DEMO_RECEIVER");
    string? seconds = Environment.GetEnvironmentVariable("OUTBOX_DEMO_LEASE_SECONDS");
    string? observationSeconds = Environment.GetEnvironmentVariable(
        "OUTBOX_DEMO_OBSERVATION_SECONDS"
    );
    await using var app = OutboxTestHost.Build(
        connection,
        Environment.GetEnvironmentVariable("OUTBOX_DEMO_URL") ?? "http://localhost:5088",
        receiverAddress is null ? null : new Uri(receiverAddress),
        worker,
        Environment.GetEnvironmentVariable("OUTBOX_DEMO_OPERATOR_TOKEN"),
        seconds is null
            ? null
            : TimeSpan.FromSeconds(
                int.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture)
            ),
        observationSeconds is null
            ? null
            : TimeSpan.FromSeconds(
                int.Parse(observationSeconds, System.Globalization.CultureInfo.InvariantCulture)
            )
    );
    await app.RunAsync();
    return;
}

if (worker)
    throw new ArgumentException("Use --serve --worker to host dispatch.");

string receiver =
    Environment.GetEnvironmentVariable("OUTBOX_DEMO_RECEIVER")
    ?? throw new InvalidOperationException(
        "Set OUTBOX_DEMO_RECEIVER to the consumer's HTTP command intake endpoint."
    );
await DemoJourneys.RunAsync(connection, new Uri(receiver), Console.Out, CancellationToken.None);
