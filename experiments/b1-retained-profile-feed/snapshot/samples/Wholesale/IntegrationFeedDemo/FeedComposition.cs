using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using Rootbolt.ActorIdentity;
using Rootbolt.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo;

public static class FeedComposition
{
    public const string Alpha = "wholesale-alpha";
    public const string Beta = "wholesale-beta";

    public static IServiceCollection AddIntegrationFeedDemo(
        this IServiceCollection services,
        string sourceConnection,
        string journalConnection
    )
    {
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddDbContext<SalesDbContext>(options =>
            SalesDatabase.Configure(options, sourceConnection)
        );
        services.AddCustomerProfiles();
        services.AddCustomerProfileFeed();
        services.AddDbContext<FeedJournalDbContext>(options =>
            options.UseNpgsql(journalConnection)
        );
        services.AddScoped<FeedJournal>();
        return services;
    }

    public static void Admit(IServiceProvider services, string organization)
    {
        if (organization is not (Alpha or Beta))
            throw new ArgumentException(
                "The finite demo admits Alpha and Beta only.",
                nameof(organization)
            );

        services
            .GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        services
            .GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.Human(new ActorId("feed-demo-owner"))));
    }

    public static async Task SetupAsync(IServiceProvider services, CancellationToken token)
    {
        await using (var migration = services.CreateAsyncScope())
        {
            await migration
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.MigrateAsync(token);
            // Only this disposable diagnostic journal uses EnsureCreated, never the Sales source.
            await migration
                .ServiceProvider.GetRequiredService<FeedJournalDbContext>()
                .Database.EnsureCreatedAsync(token);
        }

        foreach (string organization in new[] { Alpha, Beta })
        {
            await using var scope = services.CreateAsyncScope();
            Admit(scope.ServiceProvider, organization);
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            await CustomerProfileFeedSetup.ActivateAsync(database, token);
            bool seeded =
                await database
                    .Database.SqlQuery<long>(
                        $"SELECT count(*) AS \"Value\" FROM sales.customer_profiles WHERE organization_key = {organization}"
                    )
                    .SingleAsync(token) != 0;
            if (!seeded)
                SalesDemoSeed.Stage(database);

            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
    }
}
