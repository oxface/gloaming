using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed partial class SalesDispatchTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static StartStockIssueRequest Request() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 2, 1, DateTimeOffset.UtcNow.AddHours(1));

    [Fact(Timeout = 60_000)]
    public async Task RestrictedExecutableDeliversBothTenantsWithoutBusinessCredentialsOrChanges()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        var alpha = await proof.Workflow.StartAsync(Request(), token);
        var beta = await proof.Workflow.StartAsync(Request(), token, WorkflowProof.Beta);
        long tables = await proof.CountAsync(
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'sales'",
            token
        );
        await using (var services = proof.DispatchServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesOutboxDbContext>();
            Assert.Equal(
                typeof(OutboxMessageRecord),
                Assert.Single(database.Model.GetEntityTypes()).ClrType
            );
            Assert.Equal(2, await database.Set<OutboxMessageRecord>().CountAsync(token));
        }

        await using var worker = proof.StartWorker();
        await worker.WaitForOutputAsync("Application started", token);
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.CountAsync(
                    "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NOT NULL",
                    token
                ) == 2,
            token
        );
        await worker.StopAsync(token);
        foreach (int _ in new[] { 1, 2 })
            Assert.True(
                await proof.Workflow.Broker.ReceiveCommandAsync(
                    proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                    token
                )
            );

        Assert.False(
            await proof.Workflow.Broker.ReceiveCommandAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
        await using (var connection = new NpgsqlConnection(proof.Workflow.InventoryConnection))
        {
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "SELECT message_id, tenant_key FROM inventory.inbox_messages ORDER BY tenant_key",
                connection
            );
            await using var rows = await command.ExecuteReaderAsync(token);
            Assert.True(await rows.ReadAsync(token));
            Assert.Equal(alpha.CommandMessageId, rows.GetGuid(0));
            Assert.Equal(WorkflowProof.Alpha, rows.GetString(1));
            Assert.True(await rows.ReadAsync(token));
            Assert.Equal(beta.CommandMessageId, rows.GetGuid(0));
            Assert.Equal(WorkflowProof.Beta, rows.GetString(1));
            Assert.False(await rows.ReadAsync(token));
        }

        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.Workflow.ReadAsync(alpha.RequestId, token))!.Status
        );
        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.Workflow.ReadAsync(beta.RequestId, token, WorkflowProof.Beta))!.Status
        );
        Assert.Equal(
            tables,
            await proof.CountAsync(
                "SELECT count(*) FROM pg_tables WHERE schemaname = 'sales'",
                token
            )
        );
        Assert.Equal(
            6,
            await proof.CountAsync(
                "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'sales' AND c.relrowsecurity AND c.relforcerowsecurity",
                token
            )
        );
        Assert.Equal(
            2,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 1 AND lease_token IS NULL AND lease_until IS NULL",
                token
            )
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task DatabaseRejectsBusinessAccessAndEnvelopeChangesEvenWithAForgedTenantSetting()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await using (var services = proof.DispatchServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesOutboxDbContext>();
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                database
                    .Set<OutboxMessageRecord>()
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.TenantKey, WorkflowProof.Beta),
                        token
                    )
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }

        await using var connection = new NpgsqlConnection(proof.Runtime);
        await connection.OpenAsync(token);
        foreach (bool forgeSetting in new[] { false, true })
        {
            if (forgeSetting)
            {
                await using var setting = new NpgsqlCommand(
                    "SELECT set_config('app.tenant_key', 'wholesale-alpha', false)",
                    connection
                );
                await setting.ExecuteScalarAsync(token);
            }

            foreach (
                string table in new[]
                {
                    "customer_profiles",
                    "draft_orders",
                    "draft_order_grants",
                    "audit_entries",
                    "inbox_messages",
                    "stock_issue_requests",
                    "workflow_instances",
                }
            )
            foreach (
                string sql in new[]
                {
                    $"SELECT * FROM sales.{table}",
                    $"DELETE FROM sales.{table} WHERE false",
                }
            )
                await DeniedAsync(connection, sql, token);

            foreach (
                string column in new[]
                {
                    "message_id",
                    "destination",
                    "message_name",
                    "schema_version",
                    "payload",
                    "owner_key",
                    "correlation_id",
                    "causation_id",
                    "trace_parent",
                    "trace_state",
                    "queued_at",
                }
            )
                await DeniedAsync(
                    connection,
                    $"UPDATE sales.outbox_messages SET {column} = {column}",
                    token
                );

            foreach (
                string sql in new[]
                {
                    "INSERT INTO sales.outbox_messages SELECT * FROM sales.outbox_messages WHERE false",
                    "DELETE FROM sales.outbox_messages WHERE false",
                    "TRUNCATE sales.outbox_messages",
                    "CREATE TABLE sales.dispatch_must_not_create (id integer)",
                }
            )
                await DeniedAsync(connection, sql, token);
        }

        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 0 AND dispatched_at IS NULL",
                token
            )
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task RealMandatoryPublicationFailureRetainsEnvelopeAndRetriesInAFreshScope()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.Workflow.StartAsync(Request(), token);
        await using var services = proof.DispatchServices();
        await using var brokerConnection = await new ConnectionFactory
        {
            Uri = new Uri(rabbit.ConnectionString),
        }.CreateConnectionAsync(token);
        await using var channel = await brokerConnection.CreateChannelAsync(
            cancellationToken: token
        );
        string queue = proof.Environment["QueuePrefix"] + ".inventory.issue-stock";
        await channel.QueueDeleteAsync(queue, cancellationToken: token);
        var failure = await Assert.ThrowsAsync<PublishReturnException>(() =>
            SalesDispatchProof.DispatchAsync(services, token)
        );
        Assert.True(failure.IsReturn);
        Assert.Equal(312, failure.ReplyCode);
        Assert.Equal(queue, failure.RoutingKey);
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 1 AND dispatched_at IS NULL AND lease_token IS NULL AND lease_until IS NULL",
                token
            )
        );

        await proof.Workflow.Broker.SetupAsync(token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await SalesDispatchProof.DispatchAsync(services, token)
        );
        Assert.True(
            await proof.Workflow.Broker.ReceiveCommandAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                $"SELECT count(*) FROM inventory.inbox_messages WHERE message_id = '{started.CommandMessageId}'",
                token
            )
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 2 AND dispatched_at IS NOT NULL",
                token
            )
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task CompetingProcessesSkipTheLockedClaimAndEachPublishOneTenant()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await proof.Workflow.StartAsync(Request(), token, WorkflowProof.Beta);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.SalesConnection,
            insert: false,
            token,
            table: "sales.outbox_messages",
            condition: "NEW.attempts > OLD.attempts AND NEW.owner_key = 'wholesale-alpha'"
        );
        await using var first = proof.StartWorker();
        await barrier.WaitForAdvisoryAsync(1, token);
        await using var second = proof.StartWorker();
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.CountAsync(
                    "SELECT count(*) FROM sales.outbox_messages WHERE owner_key = 'wholesale-beta' AND dispatched_at IS NOT NULL",
                    token
                ) == 1,
            token
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE owner_key = 'wholesale-alpha' AND attempts = 0 AND dispatched_at IS NULL",
                token
            )
        );
        await barrier.ReleaseAsync(token);
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.CountAsync(
                    "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NOT NULL",
                    token
                ) == 2,
            token
        );
        await first.StopAsync(token);
        await second.StopAsync(token);
        Assert.Equal(
            2,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 1",
                token
            )
        );
        foreach (int _ in new[] { 1, 2 })
            Assert.True(
                await proof.Workflow.Broker.ReceiveCommandAsync(
                    proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                    token
                )
            );

        Assert.False(
            await proof.Workflow.Broker.ReceiveCommandAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
        Assert.Equal(
            2,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                "SELECT count(*) FROM inventory.inbox_messages",
                token
            )
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task DeathAfterConfirmedPublicationRetainsLeaseThenRedeliveryIsDeduplicated()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.Workflow.StartAsync(Request(), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.SalesConnection,
            insert: false,
            token,
            table: "sales.outbox_messages",
            condition: "NEW.dispatched_at IS NOT NULL"
        );
        await using (var worker = proof.StartWorker())
        {
            await barrier.WaitForAdvisoryAsync(1, token);
            Assert.True(
                await proof.Workflow.Broker.ReceiveCommandAsync(
                    proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                    token
                )
            );
            await worker.KillAsync(token);
        }

        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NULL AND lease_token IS NOT NULL AND attempts = 1",
                token
            )
        );
        await using (var services = proof.DispatchServices())
            Assert.Equal(
                OutboxDispatchResult.NoWork,
                await SalesDispatchProof.DispatchAsync(services, token)
            );

        await barrier.ReleaseAsync(token);
        // Deterministically advance the retained lease to an expired database timestamp.
        await proof.SqlAsync(
            "UPDATE sales.outbox_messages SET lease_until = clock_timestamp() - interval '1 second'",
            token
        );
        await using var recovered = proof.StartWorker();
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.CountAsync(
                    "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NOT NULL AND attempts = 2",
                    token
                ) == 1,
            token
        );
        await recovered.StopAsync(token);
        Assert.True(
            await proof.Workflow.Broker.ReceiveCommandAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                $"SELECT count(*) FROM inventory.inbox_messages WHERE message_id = '{started.CommandMessageId}'",
                token
            )
        );
        Assert.False(
            await proof.Workflow.Broker.ReceiveCommandAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
    }

    private static async Task DeniedAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken token
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(token)
        );
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }
}
