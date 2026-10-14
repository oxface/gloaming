using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Rootbolt.ActorIdentity;
using Rootbolt.Auditing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo.Tests;

public sealed partial class FeedTests
{
    [Fact]
    public async Task ForwardMigrationPreservesExistingProfilesAuditWorkflowAndOutboxWithoutInventingHistory()
    {
        string source = await postgres.CreateDatabaseAsync(Token);
        string receiver = await postgres.CreateDatabaseAsync(Token);
        await using var services = new ServiceCollection()
            .AddIntegrationFeedDemo(source, receiver)
            .AddStockReservationOrders()
            .BuildServiceProvider();
        Guid order = Guid.NewGuid();
        await using (var scope = services.CreateAsyncScope())
        {
            FeedComposition.Admit(scope.ServiceProvider, FeedComposition.Alpha);
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await database
                .GetService<IMigrator>()
                .MigrateAsync("20261010164619_AddCompensatingReservationOrders", Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            SalesDemoSeed.Stage(database);
            scope
                .ServiceProvider.GetRequiredService<IAudit<SalesDbContext>>()
                .Stage(
                    new AuditEntry(
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow,
                        scope.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current,
                        "sales",
                        "previous-change",
                        "customer",
                        SalesDemoSeed.AlphaCustomerId.ToString("D"),
                        "accepted",
                        1,
                        JsonSerializer.SerializeToElement(new { Version = 1 }),
                        tenantKey: FeedComposition.Alpha
                    )
                );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        StockReservationOrder before;
        await using (var scope = services.CreateAsyncScope())
        {
            FeedComposition.Admit(scope.ServiceProvider, FeedComposition.Alpha);
            before = await scope
                .ServiceProvider.GetRequiredService<IStockReservationOrders>()
                .StartAsync(
                    new(order, new(Guid.NewGuid(), 1, 1), new(Guid.NewGuid(), 1, 1)),
                    Token
                );
        }

        long audits = await FeedProof.ScalarAsync(
            source,
            "SELECT count(*) FROM sales.audit_entries",
            Token
        );
        long outgoing = await FeedProof.ScalarAsync(
            source,
            "SELECT count(*) FROM sales.outbox_messages",
            Token
        );
        await using (var scope = services.CreateAsyncScope())
        {
            FeedComposition.Admit(scope.ServiceProvider, FeedComposition.Alpha);
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            string[] prior = (await database.Database.GetAppliedMigrationsAsync(Token)).ToArray();
            await database.Database.MigrateAsync(Token);
            Assert.False(database.Database.HasPendingModelChanges());
            Assert.Equal(
                prior,
                (await database.Database.GetAppliedMigrationsAsync(Token)).Take(prior.Length)
            );
            Assert.Equal(
                database.Database.GetMigrations(),
                await database.Database.GetAppliedMigrationsAsync(Token)
            );
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await CustomerProfileFeedSetup.ActivateAsync(database, Token);
            await transaction.CommitAsync(Token);
            var bounds = await scope
                .ServiceProvider.GetRequiredService<ICustomerProfileFeed>()
                .DescribeAsync(Token);
            Assert.Equal(0, bounds.Head.AfterPosition);
            Assert.Empty(
                Assert
                    .IsType<CustomerProfileFeedRead.Page>(
                        await scope
                            .ServiceProvider.GetRequiredService<ICustomerProfileFeed>()
                            .ReadAsync(bounds.Head, 2, Token)
                    )
                    .Changes
            );
            Assert.Equal(
                before,
                await scope
                    .ServiceProvider.GetRequiredService<IStockReservationOrders>()
                    .ReadAsync(order, Token)
            );
            Assert.Equal(
                "Alpha Customer",
                (
                    await scope
                        .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                        .ReadAsync(SalesDemoSeed.AlphaCustomerId, Token)
                )!.DisplayName
            );
        }

        Assert.Equal(
            audits,
            await FeedProof.ScalarAsync(source, "SELECT count(*) FROM sales.audit_entries", Token)
        );
        Assert.Equal(
            outgoing,
            await FeedProof.ScalarAsync(source, "SELECT count(*) FROM sales.outbox_messages", Token)
        );
    }
}
