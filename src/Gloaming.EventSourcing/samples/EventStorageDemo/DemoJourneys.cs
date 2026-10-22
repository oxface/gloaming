using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventStorageDemo;

public static class DemoJourneys
{
    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var options = StorageDbContext.Options(connection);
        await using (var setup = new StorageDbContext(options))
            await setup.Database.MigrateAsync(cancellationToken);
        await using (var write = new StorageDbContext(options))
        {
            await using var transaction = await write.Database.BeginTransactionAsync(
                cancellationToken
            );
            try
            {
                if (!await write.Streams.AnyAsync(cancellationToken))
                {
                    DemoData.Stage(write);
                    await write.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        await using var read = new StorageDbContext(options);
        var counter = await read
            .Streams.AsNoTracking()
            .SingleAsync(
                row => row.Id == DemoData.CounterId && row.StreamType == "proof.counter",
                cancellationToken
            );
        var facts = await read
            .Events.AsNoTracking()
            .Where(row => row.StreamId == counter.Id)
            .OrderBy(row => row.StreamVersion)
            .ToArrayAsync(cancellationToken);
        int value =
            facts[0].Payload.GetProperty("value").GetInt32()
            + facts[1].Payload.GetProperty("amount").GetInt32();
        var note = await read
            .Streams.AsNoTracking()
            .SingleAsync(
                row => row.Id == DemoData.NoteId && row.StreamType == "proof.note",
                cancellationToken
            );
        var noteFact = await read
            .Events.AsNoTracking()
            .SingleAsync(row => row.StreamId == note.Id, cancellationToken);
        await output.WriteLineAsync(
            $"journal proof.counter: version={counter.Version}, value={value}"
        );
        await output.WriteLineAsync(
            $"journal proof.note: version={note.Version}, text={noteFact.Payload.GetProperty("text").GetString()}"
        );
    }
}
