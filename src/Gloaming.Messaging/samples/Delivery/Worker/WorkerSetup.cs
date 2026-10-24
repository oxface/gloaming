using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Messaging.Exports;
using Gloaming.Samples.Messaging.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Gloaming.Samples.Messaging.Worker;

internal static class WorkerSetup
{
    internal static string Connection(IConfiguration configuration, string module) =>
        configuration.GetConnectionString(module)
        ?? throw new InvalidOperationException($"Configure ConnectionStrings:{module}.");

    internal static async Task RunAsync(
        IConfiguration configuration,
        CancellationToken cancellation
    )
    {
        await using (var exports = ExportDbContext.Create(Connection(configuration, "Exports")))
            await exports.Database.MigrateAsync(cancellation);

        await using (var rendering = RenderDbContext.Create(Connection(configuration, "Rendering")))
            await rendering.Database.MigrateAsync(cancellation);

        await using var broker = await BrokerSession.OpenAsync(
            configuration,
            createQueue: true,
            cancellation
        );
        Console.WriteLine(
            "Messaging schemas and durable queue configured. No business rows seeded."
        );
    }

    internal static async Task RequireRenderingAsync(
        string connection,
        CancellationToken cancellation
    )
    {
        await using var database = RenderDbContext.Create(connection);
        _ = await database.Set<InboxMessageRecord>().AnyAsync(cancellation);
        _ = await database.Set<RenderJob>().AnyAsync(cancellation);
    }
}

public sealed class WorkerAssemblyMarker;
