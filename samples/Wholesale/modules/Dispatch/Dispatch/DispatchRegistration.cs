using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Dispatch;

public static class DispatchRegistration
{
    public const string DirectorySubscription = "dispatch.customer-directory";

    public static IServiceCollection AddDispatchDirectory(
        this IServiceCollection services,
        string connection
    )
    {
        services.AddDbContext<DispatchDbContext>(options => Configure(options, connection));
        services.AddScoped<ICustomerDirectory, CustomerDirectory>();
        services.AddSingleton<ICustomerDirectoryIntake, DirectoryIntake>();
        services.AddPostgresInbox<DispatchDbContext>();
        services.AddPostgresInboxProcessor<DispatchDbContext>(new(TimeSpan.FromMilliseconds(50)));
        return services.AddInboxHandler<DispatchDbContext, DirectoryHandler>(DirectorySubscription);
    }

    public static void Configure(DbContextOptionsBuilder options, string connection) =>
        options.UseNpgsql(
            connection,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "dispatch")
        );
}

public sealed class DispatchDesignTimeFactory : IDesignTimeDbContextFactory<DispatchDbContext>
{
    public DispatchDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DispatchDbContext>();
        DispatchRegistration.Configure(
            options,
            "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
        );
        return new DispatchDbContext(options.Options);
    }
}
