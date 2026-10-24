namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed partial class SalesDispatchTests
{
    [Fact(Timeout = 60_000)]
    public async Task RestrictedBrokerExecutableLeavesUnselectedRouteUnclaimedWhileStockPublicationProgresses()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await proof.SqlAsync(
            """
            INSERT INTO sales.outbox_messages (message_id, destination, message_name, schema_version, payload, owner_key, attempts)
            VALUES (gen_random_uuid(), 'unselected.destination', 'unselected.message', 1, '{}', 'wholesale-alpha', 0)
            """,
            token
        );
        await using var worker = proof.StartWorker();
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.CountAsync(
                    "SELECT count(*) FROM sales.outbox_messages WHERE destination = 'inventory.issue-stock' AND dispatched_at IS NOT NULL",
                    token
                ) == 1,
            token
        );
        await worker.StopAsync(token);
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE destination = 'unselected.destination' AND attempts = 0 AND revision = 0 AND lease_token IS NULL AND lease_until IS NULL AND dispatched_at IS NULL",
                token
            )
        );
    }
}
