using System.Globalization;
using Gloaming.Samples.Messaging.Rendering;
using Gloaming.Samples.Messaging.Tests.Fixtures.Inbox;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--cleanup", StringComparer.Ordinal))
{
    if (args.Length != 1)
        throw new ArgumentException("Use --cleanup as a separate finite maintenance role.");

    string maintenance =
        Environment.GetEnvironmentVariable("INBOX_DEMO_MAINTENANCE_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "Set INBOX_DEMO_MAINTENANCE_CONNECTION_STRING for cleanup."
        );
    string? cutoff = Environment.GetEnvironmentVariable("INBOX_DEMO_PROCESSED_BEFORE");
    if (
        !DateTimeOffset.TryParseExact(
            cutoff,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var processedBefore
        )
    )
        throw new ArgumentException(
            "Set INBOX_DEMO_PROCESSED_BEFORE to an explicit UTC timestamp ending in Z."
        );

    int batchSize = int.Parse(
        Environment.GetEnvironmentVariable("INBOX_DEMO_CLEANUP_BATCH_SIZE")
            ?? throw new InvalidOperationException(
                "Set INBOX_DEMO_CLEANUP_BATCH_SIZE from 1 through 1000."
            ),
        CultureInfo.InvariantCulture
    );
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await RenderInboxRetention.RunAsync(
        maintenance,
        processedBefore,
        batchSize,
        Console.Out,
        deadline.Token
    );
    return;
}

string connection =
    Environment.GetEnvironmentVariable("INBOX_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set INBOX_DEMO_CONNECTION_STRING to a disposable PostgreSQL database."
    );
if (args.Contains("--setup", StringComparer.Ordinal))
{
    await using var database = RenderDbContext.Create(connection);
    await database.Database.MigrateAsync();
    Console.WriteLine("Rendering schema migrated.");
    return;
}
await using var app = InboxTestHost.Build(
    connection,
    Environment.GetEnvironmentVariable("INBOX_DEMO_URL") ?? "http://localhost:5087",
    args.Contains("--worker", StringComparer.Ordinal),
    Environment.GetEnvironmentVariable("INBOX_DEMO_OPERATOR_TOKEN")
);
await app.RunAsync();
