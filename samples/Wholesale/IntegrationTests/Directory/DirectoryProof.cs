using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Directory;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Directory;

internal sealed class DirectoryProof : IAsyncDisposable
{
    internal const string Alpha = "wholesale-alpha";
    internal const string Beta = "wholesale-beta";
    internal ServiceProvider Services { get; }
    internal string SourceConnection { get; }
    internal string ReceiverConnection { get; }

    private DirectoryProof(string source, string receiver)
    {
        SourceConnection = source;
        ReceiverConnection = receiver;
        Services = new ServiceCollection()
            .AddDeliveryDirectory(source, receiver)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    internal static async Task<DirectoryProof> CreateAsync(
        PostgreSqlFixture postgres,
        CancellationToken token
    )
    {
        var proof = new DirectoryProof(
            await postgres.CreateDatabaseAsync(token),
            await postgres.CreateDatabaseAsync(token)
        );
        await using var scope = proof.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var receiver = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
        await source.Database.MigrateAsync(token);
        await receiver.Database.MigrateAsync(token);
        Assert.False(source.Database.HasPendingModelChanges());
        Assert.False(receiver.Database.HasPendingModelChanges());
        return proof;
    }

    internal async Task<T> OperationAsync<T>(
        string organization,
        Func<IServiceProvider, Task<T>> action
    )
    {
        await using var scope = Services.CreateAsyncScope();
        // Trusted test setup supplies an admitted human for the existing Sales mutation policy.
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new(Actor.Human(new ActorId("directory-proof-human"))));
        return await action(scope.ServiceProvider);
    }

    internal Task<CustomerProfile> RegisterAsync(
        Guid id,
        string code,
        string organization,
        CancellationToken token
    ) =>
        OperationAsync(
            organization,
            services =>
                services
                    .GetRequiredService<ICustomerDeliveryDirectory>()
                    .RegisterAsync(
                        new(id, Guid.NewGuid(), code, code + " Customer", code + " Street"),
                        token
                    )
        );

    internal Task<CustomerProfile> EditAsync(
        CustomerProfile before,
        string name,
        CancellationToken token
    ) =>
        OperationAsync(
            Alpha,
            async services =>
            {
                var result = await services
                    .GetRequiredService<ICustomerProfiles>()
                    .ChangeAsync(
                        new(
                            before.CustomerId,
                            before.AddressId,
                            before.Version,
                            name,
                            name + " Street"
                        ),
                        token
                    );
                return Assert.IsType<ProfileChangeResult.Updated>(result).Profile;
            }
        );

    internal Task<CustomerProfile> ListedAsync(
        CustomerProfile before,
        bool listed,
        CancellationToken token
    ) =>
        OperationAsync(
            Alpha,
            async services =>
            {
                var result = await services
                    .GetRequiredService<ICustomerDeliveryDirectory>()
                    .SetListedAsync(before.CustomerId, before.Version, listed, token);
                return Assert.IsType<ProfileChangeResult.Updated>(result).Profile;
            }
        );

    internal Task<DirectoryProgress> BeginAsync(string organization, CancellationToken token) =>
        OperationAsync(
            organization,
            services => services.GetRequiredService<ICustomerDirectory>().BeginAsync(token)
        );

    internal Task<CustomerDeliveryPage> PageAsync(Guid? after, int size, CancellationToken token) =>
        OperationAsync(
            Alpha,
            services =>
                services
                    .GetRequiredService<ICustomerDeliveryDirectory>()
                    .ReadPageAsync(after, size, token)
        );

    internal Task<DirectoryProgress> ApplyAsync(
        DirectoryProgress progress,
        CustomerDeliveryPage page,
        CancellationToken token
    ) =>
        OperationAsync(
            Alpha,
            async services =>
            {
                var directory = services.GetRequiredService<ICustomerDirectory>();
                await directory.ApplyPageAsync(progress.PopulationId, progress.After, page, token);
                return await directory.ProgressAsync(token);
            }
        );

    internal Task<IReadOnlyList<DispatchCustomer>> SearchAsync(
        string organization,
        CancellationToken token
    ) =>
        OperationAsync(
            organization,
            services => services.GetRequiredService<ICustomerDirectory>().SearchAsync("", token)
        );

    internal async Task<OutboxDispatchResult> DispatchAsync(CancellationToken token)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IOutboxDispatcher<SalesDbContext>>()
            .DispatchNextAsync(token);
    }

    internal async Task<InboxProcessingResult> ProcessAsync(CancellationToken token)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<DispatchDbContext>>()
            .ProcessNextAsync(DispatchRegistration.DirectorySubscription, token);
    }

    internal async Task DrainAsync(CancellationToken token)
    {
        while (await DispatchAsync(token) != OutboxDispatchResult.NoWork) { }

        while (await ProcessAsync(token) != InboxProcessingResult.NoWork) { }
    }

    internal Task ReceiveAsync(
        Guid id,
        CustomerDeliveryState state,
        CancellationToken token,
        string organization = Alpha
    ) =>
        Services
            .GetRequiredService<ICustomerDirectoryIntake>()
            .ReceiveAsync(id, organization, state, token);

    internal static async Task SqlAsync(
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

    internal static async Task<long> CountAsync(
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

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}
