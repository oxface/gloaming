using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed partial class SalesDispatchTests
{
    [Fact]
    public async Task ExistingDispatchCredentialCannotDeleteThroughTheRetentionOperation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await using var services = proof.DispatchServices();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SalesOutboxDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.DeleteCompletedPostgresOutboxBatchAsync(
                "inventory.issue-stock",
                new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                1,
                token
            )
        );
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 0 AND lease_token IS NULL",
                token
            )
        );
    }
}
