using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Tests.Infrastructure;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Local;

public sealed class RouteSelectionTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task LocalSalesPublisherLeavesUnselectedRouteUnclaimedWhileReservationProgresses()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, token);
        var first = await proof.SeedAsync(token);
        var second = await proof.SeedAsync(token);
        await proof.StartAsync(LocalWorkflowProof.Request(first, second), token);
        await LocalWorkflowProof.SqlAsync(
            proof.Sales,
            """
            INSERT INTO sales.outbox_messages (message_id, destination, message_name, schema_version, payload, owner_key, attempts)
            VALUES (gen_random_uuid(), 'unselected.destination', 'unselected.message', 1, '{}', 'wholesale-alpha', 0)
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
            await LocalWorkflowProof.CountAsync(
                proof.Sales,
                "SELECT count(*) FROM sales.outbox_messages WHERE destination = 'unselected.destination' AND attempts = 0 AND revision = 0 AND lease_token IS NULL AND lease_until IS NULL AND dispatched_at IS NULL",
                token
            )
        );
    }
}
