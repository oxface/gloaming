using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo.Tests;

internal sealed class FeedProof(ServiceProvider services, string source, string receiver)
    : IAsyncDisposable
{
    internal static readonly Guid OtherCustomer = Guid.Parse(
        "10000000-0000-0000-0000-000000000003"
    );
    internal static readonly Guid OtherAddress = Guid.Parse("20000000-0000-0000-0000-000000000003");
    internal ServiceProvider Services => services;
    internal string Source => source;
    internal string Receiver => receiver;
    internal Dictionary<string, string> Environment =>
        new() { ["ConnectionStrings__Sales"] = source, ["ConnectionStrings__Journal"] = receiver };

    internal static async Task<FeedProof> CreateAsync(
        PostgreSqlFixture postgres,
        CancellationToken token,
        IInterceptor? sourceInterceptor = null,
        IInterceptor? journalInterceptor = null
    )
    {
        string source = await postgres.CreateDatabaseAsync(token);
        string journal = await postgres.CreateDatabaseAsync(token);
        var registrations = new ServiceCollection().AddIntegrationFeedDemo(source, journal);
        if (sourceInterceptor is not null)
            registrations.AddDbContext<SalesDbContext>(options =>
                options.AddInterceptors(sourceInterceptor)
            );

        if (journalInterceptor is not null)
            registrations.AddDbContext<FeedJournalDbContext>(options =>
                options.AddInterceptors(journalInterceptor)
            );

        var services = registrations.BuildServiceProvider();
        try
        {
            await FeedComposition.SetupAsync(services, token);
            await ExecuteAsync(
                source,
                """
                INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
                VALUES ('10000000-0000-0000-0000-000000000003', 'wholesale-alpha', 'OTHER', 'Other Customer', 1);
                INSERT INTO sales.customer_addresses (id, organization_key, customer_id, address_line)
                VALUES ('20000000-0000-0000-0000-000000000003', 'wholesale-alpha', '10000000-0000-0000-0000-000000000003', 'Other Street');
                """,
                token
            );
            return new(services, source, journal);
        }
        catch
        {
            await services.DisposeAsync();
            throw;
        }
    }

    internal AsyncServiceScope Scope(string organization = FeedComposition.Alpha)
    {
        var scope = services.CreateAsyncScope();
        FeedComposition.Admit(scope.ServiceProvider, organization);
        return scope;
    }

    internal async Task<ProfileChangeResult> WriteAsync(
        long version,
        CancellationToken token,
        string name = "Changed",
        bool other = false,
        string organization = FeedComposition.Alpha
    )
    {
        await using var scope = Scope(organization);
        return await scope
            .ServiceProvider.GetRequiredService<ICustomerProfiles>()
            .ChangeAsync(
                new(
                    other ? OtherCustomer
                        : organization == FeedComposition.Alpha ? SalesDemoSeed.AlphaCustomerId
                        : SalesDemoSeed.BetaCustomerId,
                    other ? OtherAddress
                        : organization == FeedComposition.Alpha ? SalesDemoSeed.AlphaAddressId
                        : SalesDemoSeed.BetaAddressId,
                    version,
                    name,
                    "Changed Street"
                ),
                token
            );
    }

    internal async Task<CustomerProfileFeedBounds> BoundsAsync(
        CancellationToken token,
        string organization = FeedComposition.Alpha
    )
    {
        await using var scope = Scope(organization);
        return await scope
            .ServiceProvider.GetRequiredService<ICustomerProfileFeed>()
            .DescribeAsync(token);
    }

    internal async Task<CustomerProfileFeedRead> ReadAsync(
        CustomerProfileFeedCursor cursor,
        CancellationToken token,
        int size = 2,
        string organization = FeedComposition.Alpha
    )
    {
        await using var scope = Scope(organization);
        return await scope
            .ServiceProvider.GetRequiredService<ICustomerProfileFeed>()
            .ReadAsync(cursor, size, token);
    }

    internal async Task<CustomerProfileFeedRead.Page> PageAsync(
        CustomerProfileFeedCursor cursor,
        CancellationToken token,
        int size = 2
    ) => Assert.IsType<CustomerProfileFeedRead.Page>(await ReadAsync(cursor, token, size));

    internal async Task InitializeJournalAsync(
        Guid id,
        CustomerProfileFeedCursor cursor,
        CancellationToken token
    )
    {
        await using var scope = Scope();
        await scope
            .ServiceProvider.GetRequiredService<FeedJournal>()
            .InitializeAsync(id, FeedComposition.Alpha, cursor, token);
    }

    internal async Task RecordAsync(
        Guid id,
        CustomerProfileFeedCursor expected,
        CustomerProfileFeedRead.Page page,
        CancellationToken token
    )
    {
        await using var scope = Scope();
        await scope
            .ServiceProvider.GetRequiredService<FeedJournal>()
            .RecordAndCheckpointAsync(id, FeedComposition.Alpha, expected, page, token);
    }

    internal async Task<(CustomerProfileFeedCursor Cursor, long Count)> JournalAsync(
        Guid id,
        CancellationToken token
    )
    {
        await using var scope = Scope();
        var journal = scope.ServiceProvider.GetRequiredService<FeedJournal>();
        return (
            await journal.ReadAsync(id, FeedComposition.Alpha, token),
            await journal.CountAsync(id, token)
        );
    }

    internal async Task PruneAsync(CustomerProfileFeedCursor through, CancellationToken token)
    {
        await using var scope = Scope();
        await CustomerProfileFeedSetup.PruneThroughAsync(
            scope.ServiceProvider.GetRequiredService<SalesDbContext>(),
            through,
            token
        );
    }

    internal static async Task ExecuteAsync(
        string connectionString,
        string sql,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<long> ScalarAsync(
        string connectionString,
        string sql,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(token),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    public async ValueTask DisposeAsync() => await services.DisposeAsync();
}
