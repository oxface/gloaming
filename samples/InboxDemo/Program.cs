using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.InboxDemo;

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
await using var app = InboxDemoHost.Build(
    connection,
    Environment.GetEnvironmentVariable("INBOX_DEMO_URL") ?? "http://localhost:5087",
    args.Contains("--worker", StringComparer.Ordinal),
    Environment.GetEnvironmentVariable("INBOX_DEMO_OPERATOR_TOKEN")
);
await app.RunAsync();
