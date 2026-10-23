using Gloaming.Samples.EventStorageDemo;
using Microsoft.EntityFrameworkCore;

string connection =
    Environment.GetEnvironmentVariable("EVENT_STORAGE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Supply a disposable EVENT_STORAGE_DEMO_CONNECTION_STRING."
    );
await using (var setup = new StorageDbContext(StorageDbContext.Options(connection)))
    await setup.Database.MigrateAsync();

await CounterAppendJourney.RunAsync(connection, Console.Out, CancellationToken.None);
