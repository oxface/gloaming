using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory;

public static class InventoryDatabase
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")
        );
}
