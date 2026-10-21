using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.OutboxDemo;

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
    await using var app = OutboxDemoHost.Build(
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
