using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs.TenantIsolation;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Queries;

public sealed partial class TenantIsolationTests
{
    [Fact]
    public async Task RestrictedReceiptReplayRemainsTenantIsolatedAndRuntimeCannotReplaceOrDeleteResults()
    {
        await using var proof = await CreateAsync();
        Guid operationId = Guid.NewGuid();
        await using (
            var administrator = CqrsComposition
                .CreateServices(proof.AdminConnection)
                .BuildServiceProvider()
        )
        {
            foreach (string tenant in new[] { "wholesale-alpha", "wholesale-beta" })
            {
                await using var scope = TenantIsolationJourney.CreateScope(
                    administrator,
                    tenant,
                    tenant == "wholesale-alpha" ? "application-alpha" : "application-beta"
                );
                var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
                await using var transaction = await database.Database.BeginTransactionAsync(Token);
                SalesDemoSeed.StageOrderAllowance(database, 100);
                await database.SaveChangesAsync(Token);
                Assert.IsType<DraftOrderChangeResult.Changed>(
                    await scope
                        .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                        .SubmitAsync(new(DraftOrderAccessDemoSeed.OrderId, 1, operationId), Token)
                );
                await database.SaveChangesAsync(Token);
                await transaction.CommitAsync(Token);
            }
        }

        await using var provider = proof.Provider();
        await using (
            var alpha = TenantIsolationJourney.CreateScope(
                provider,
                "wholesale-alpha",
                "application-alpha"
            )
        )
        {
            var database = alpha.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await database.EstablishTenantDatabaseContextAsync(
                alpha.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                "app.tenant_key",
                Token
            );
            var repeated = Assert.IsType<DraftOrderChangeResult.Changed>(
                await alpha
                    .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                    .SubmitAsync(new(DraftOrderAccessDemoSeed.OrderId, 1, operationId), Token)
            );
            Assert.True(repeated.Proposed.IsSubmitted);
            Assert.Equal(40, repeated.Proposed.Total);
            Assert.Equal(SalesDemoSeed.AlphaCustomerId, repeated.Proposed.CustomerId);
            Assert.Equal(
                "wholesale-alpha",
                (
                    await database
                        .Set<CommandReceiptRecord>()
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .SingleAsync(Token)
                ).ScopeKey
            );
            Assert.Equal(
                1,
                await database
                    .Database.SqlQueryRaw<int>(
                        "SELECT count(*)::int AS \"Value\" FROM sales.command_receipts"
                    )
                    .SingleAsync(Token)
            );
        }

        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(
            0,
            await ValueAsync<int>(
                connection,
                null,
                "SELECT count(*)::int FROM sales.command_receipts"
            )
        );
        await DeniedAsync(() =>
            CommandAsync(connection, null, "DELETE FROM sales.command_receipts WHERE false")
        );
        await DeniedAsync(() =>
            CommandAsync(
                connection,
                null,
                "UPDATE sales.command_receipts SET result = '{}' WHERE false"
            )
        );
        await using var forged = await connection.BeginTransactionAsync(Token);
        await SelectTenantAsync(connection, forged, "wholesale-alpha");
        await DeniedAsync(() =>
            CommandAsync(
                connection,
                forged,
                "INSERT INTO sales.command_receipts (organization_key, operation, operation_id, format_version, input, result) VALUES ('wholesale-beta', 'submit-draft', @id, 1, '{}', '{}')",
                new NpgsqlParameter("id", Guid.NewGuid())
            )
        );
    }
}
