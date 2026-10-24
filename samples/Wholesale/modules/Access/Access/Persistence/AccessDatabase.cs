using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Access.Persistence;

public static class AccessDatabase
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "access")
        );
}
