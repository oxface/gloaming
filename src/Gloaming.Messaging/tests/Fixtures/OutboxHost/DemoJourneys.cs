using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Messaging.Exports;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Messaging.Tests.Fixtures.Outbox;

public static class DemoJourneys
{
    public static async Task RunAsync(
        string connection,
        Uri receiver,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        Guid id = Guid.NewGuid();
        await using (var writer = ExportDbContext.Create(connection))
        {
            // Explicit finite sample setup, never library/worker startup behavior.
            await writer.Database.MigrateAsync(cancellationToken);
            writer.Add(ExportRequest.Create(id, 3));
            await writer.SaveChangesAsync(cancellationToken);
        }
        await using (var writer = ExportDbContext.Create(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(
                cancellationToken
            );
            var requests = new ExportRequestCommands(writer, new EfOutbox<ExportDbContext>(writer));
            if (
                await requests.SubmitAsync(id, 1, cancellationToken)
                != ExportSubmissionResult.Accepted
            )
                throw new InvalidOperationException("The draft export was not accepted.");
            await writer.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await using var dispatchContext = ExportDbContext.Create(connection);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var dispatcher = new PostgresOutboxDispatcher<ExportDbContext>(
            dispatchContext,
            new HttpCommandPublisher(client, receiver),
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
            failurePolicy: new ExportOutboxFailurePolicy()
        );
        var result = await dispatcher.DispatchNextAsync(cancellationToken);
        output.WriteLine($"export {id}: submitted; one dispatch={result}");
    }
}
