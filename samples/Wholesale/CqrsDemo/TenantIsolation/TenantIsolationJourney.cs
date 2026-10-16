using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.CqrsDemo.TenantIsolation;

public static class TenantIsolationJourney
{
    public static async Task RunAsync(
        string migrationConnection,
        string runtimeConnection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(output);

        await InitializeAsync(migrationConnection, runtimeConnection, cancellationToken);
        await using var dataSource = NpgsqlDataSource.Create(runtimeConnection);
        await SalesRowSecurity.VerifyRuntimeAsync(dataSource, cancellationToken);
        await using var provider = CreateRuntimeProvider(dataSource);
        foreach (string organization in new[] { "wholesale-alpha", "wholesale-beta" })
        {
            await using var operation = CreateScope(provider, organization, "application-alpha");
            var database = operation.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            await database.EstablishTenantDatabaseContextAsync(
                operation.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                "app.tenant_key",
                cancellationToken
            );
            int isolated = await database
                .Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM sales.draft_orders"
                )
                .SingleAsync(cancellationToken);
            var permitted = await operation
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(new(), cancellationToken);
            int expected = organization == "wholesale-alpha" ? 1 : 0;
            if (isolated != 1 || permitted.TotalCount != expected)
                throw new InvalidOperationException(
                    "Tenant isolation and resource permission results differed from the demo."
                );

            await transaction.CommitAsync(cancellationToken);
            await output.WriteLineAsync(
                $"{organization}: database rows={isolated}, Alpha's permitted drafts={permitted.TotalCount}."
            );
        }
    }

    internal static async Task InitializeAsync(
        string migrationConnection,
        string runtimeConnection,
        CancellationToken cancellationToken
    )
    {
        var runtime = new NpgsqlConnectionStringBuilder(runtimeConnection);
        if (
            runtime.NoResetOnClose
            || runtime.Multiplexing
            || !string.IsNullOrEmpty(runtime.Options)
        )
            throw new ArgumentException(
                "This composition requires connection reset and excludes multiplexing.",
                nameof(runtimeConnection)
            );

        await using var owner = CqrsComposition
            .CreateServices(migrationConnection)
            .BuildServiceProvider();
        await using (var migrate = owner.CreateAsyncScope())
            await migrate
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.MigrateAsync(cancellationToken);

        foreach (string organization in new[] { "wholesale-alpha", "wholesale-beta" })
        {
            await using var seed = CreateScope(owner, organization, "demo-setup");
            var database = seed.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            SalesDemoSeed.Stage(database);
            DraftOrderAccessDemoSeed.Stage(database);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await SalesRowSecurity.ApplyAsync(
            migrationConnection,
            runtime.Username!,
            cancellationToken
        );
    }

    internal static ServiceProvider CreateRuntimeProvider(
        NpgsqlDataSource dataSource,
        bool retry = false
    )
    {
        var options = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
        if (
            options.NoResetOnClose
            || options.Multiplexing
            || !string.IsNullOrEmpty(options.Options)
        )
            throw new ArgumentException(
                "Use reset-enabled, non-multiplexed runtime connections.",
                nameof(dataSource)
            );

        var services = CqrsComposition.CreateServices(dataSource.ConnectionString);
        services.AddDbContext<SalesDbContext>(builder =>
            builder.UseNpgsql(
                dataSource,
                postgres =>
                {
                    if (retry)
                        postgres.EnableRetryOnFailure(1, TimeSpan.Zero, null);
                }
            )
        );
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    // Finite demo admission. Production hosts authenticate/admit before creating this operation.
    internal static AsyncServiceScope CreateScope(
        ServiceProvider provider,
        string organization,
        string human
    )
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new(organization)));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new(Actor.Human(new(human))));
        return scope;
    }
}
