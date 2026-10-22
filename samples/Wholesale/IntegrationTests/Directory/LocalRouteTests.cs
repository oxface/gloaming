using Gloaming.Messaging.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Local;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Directory;

public sealed class LocalRouteTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task LocalSalesPublisherLeavesDirectoryRowUnclaimedWhileReservationProgresses()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, token);
        var first = await proof.SeedAsync(token);
        var second = await proof.SeedAsync(token);
        await proof.StartAsync(LocalWorkflowProof.Request(first, second), token);
        await DirectoryProof.SqlAsync(
            proof.Sales,
            """
            INSERT INTO sales.outbox_messages (message_id, destination, message_name, schema_version, payload, owner_key, attempts)
            VALUES (gen_random_uuid(), 'dispatch.customer-directory', 'sales.customer-delivery-state', 1, '{}', 'wholesale-alpha', 0)
            """,
            token
        );
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.DispatchAsync<SalesDbContext>(token)
        );
        Assert.Equal(OutboxDispatchResult.NoWork, await proof.DispatchAsync<SalesDbContext>(token));
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.Sales,
                "SELECT count(*) FROM sales.outbox_messages WHERE destination = 'dispatch.customer-directory' AND attempts = 0 AND revision = 0 AND lease_token IS NULL AND lease_until IS NULL AND dispatched_at IS NULL",
                token
            )
        );
    }
}
