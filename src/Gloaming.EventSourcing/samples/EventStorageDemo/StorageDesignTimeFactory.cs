using Microsoft.EntityFrameworkCore.Design;

namespace ModulithFoundry.Samples.EventStorageDemo;

public sealed class StorageDesignTimeFactory : IDesignTimeDbContextFactory<StorageDbContext>
{
    public StorageDbContext CreateDbContext(string[] args) =>
        new(
            StorageDbContext.Options(
                Environment.GetEnvironmentVariable("EVENT_STORAGE_DEMO_CONNECTION_STRING")
                    ?? "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
            )
        );
}
