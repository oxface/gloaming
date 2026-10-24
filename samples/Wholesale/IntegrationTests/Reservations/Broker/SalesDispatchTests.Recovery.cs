using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed partial class SalesDispatchTests
{
    [Fact]
    public async Task RestrictedDispatchAccountsBothTenantsUnderCommandRlsWithoutOperatorOrBusinessAuthority()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await proof.Workflow.StartAsync(Request(), token, WorkflowProof.Beta);
        await proof.SqlAsync(
            """
            ALTER TABLE sales.outbox_messages ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sales.outbox_messages FORCE ROW LEVEL SECURITY;
            CREATE POLICY gloaming_outbox_enqueue ON sales.outbox_messages FOR INSERT TO PUBLIC
                WITH CHECK (owner_key COLLATE "C" = NULLIF(current_setting('app.tenant_key', true), ''));
            CREATE POLICY gloaming_outbox_read ON sales.outbox_messages FOR SELECT TO PUBLIC USING (true);
            CREATE POLICY gloaming_outbox_delivery ON sales.outbox_messages FOR UPDATE TO PUBLIC USING (true) WITH CHECK (true);
            """,
            token
        );
        var services = new ServiceCollection();
        services.AddDbContext<SalesOutboxDbContext>(options => options.UseNpgsql(proof.Runtime));
        services.AddPostgresOutboxDispatcher<SalesOutboxDbContext, FailingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        services.AddOutboxFailurePolicy<SalesOutboxDbContext, StopPolicy>();
        services.AddPostgresOutboxDeadLetters<SalesOutboxDbContext>();
        await using var provider = services.BuildServiceProvider();
        for (int i = 0; i < 2; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await Assert.ThrowsAsync<IOException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IOutboxDispatcher<SalesOutboxDbContext>>()
                    .DispatchNextAsync(token)
            );
        }

        Assert.Equal(
            2,
            await proof.CountAsync(
                "SELECT count(DISTINCT owner_key) FROM sales.outbox_messages WHERE failure_count = 1 AND failures_since_redrive = 1 AND attempts = 1 AND dead_lettered_at IS NOT NULL AND lease_token IS NULL",
                token
            )
        );
        await using (var scope = provider.CreateAsyncScope())
        {
            var letters = scope.ServiceProvider.GetRequiredService<
                IOutboxDeadLetters<SalesOutboxDbContext>
            >();
            var database = scope.ServiceProvider.GetRequiredService<SalesOutboxDbContext>();
            string route = await database
                .Set<OutboxMessageRecord>()
                .Select(row => row.RouteKey)
                .FirstAsync(token);
            var page = await letters.ListAsync(route, new(), token);
            var item = Assert.Single(page.Items.Take(1));
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                letters.RetryAsync(route, item.MessageId, item.Revision, token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }

        await using var native = new NpgsqlConnection(proof.Runtime);
        await native.OpenAsync(token);
        foreach (
            string sql in new[]
            {
                "UPDATE sales.outbox_messages SET payload = '{}'",
                "SELECT * FROM sales.customer_profiles",
                "UPDATE sales.outbox_messages SET redrive_count = 1",
            }
        )
        {
            await using var command = new NpgsqlCommand(sql, native);
            Assert.Equal(
                PostgresErrorCodes.InsufficientPrivilege,
                (
                    await Assert.ThrowsAsync<PostgresException>(() =>
                        command.ExecuteNonQueryAsync(token)
                    )
                ).SqlState
            );
        }
    }

    [Fact]
    public async Task MissingAccountingGrantFaultsTheWorkerWithoutPartialAccounting()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        using var identifiers = new NpgsqlCommandBuilder();
        string login = identifiers.QuoteIdentifier(
            new NpgsqlConnectionStringBuilder(proof.Runtime).Username!
        );
        await proof.SqlAsync(
            $"REVOKE UPDATE (failure_count) ON sales.outbox_messages FROM {login}",
            token
        );
        int publications = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SalesOutboxDbContext>(options => options.UseNpgsql(proof.Runtime));
        services.AddScoped(_ => new FailingPublisher
        {
            OnPublish = () => Interlocked.Increment(ref publications),
        });
        services.AddPostgresOutboxDispatcher<SalesOutboxDbContext, FailingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        services.AddOutboxWorker<SalesOutboxDbContext>(
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        await using var provider = services.BuildServiceProvider();
        var worker = Assert.IsAssignableFrom<BackgroundService>(
            Assert.Single(provider.GetServices<IHostedService>())
        );
        await worker.StartAsync(token);
        try
        {
            var error = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() =>
                worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), token)
            );
            Assert.Equal(
                PostgresErrorCodes.InsufficientPrivilege,
                Assert.IsType<PostgresException>(error.AccountingFailure).SqlState
            );
        }
        finally
        {
            await worker.StopAsync(token);
        }

        Assert.Equal(1, publications);
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 1 AND revision = 1 AND failure_count = 0 AND last_failure_code IS NULL AND dead_lettered_at IS NULL AND lease_token IS NOT NULL",
                token
            )
        );
    }

    public sealed class StopPolicy : IOutboxFailurePolicy<SalesOutboxDbContext>
    {
        public OutboxFailureDecision Decide(OutboxFailure failure) =>
            new OutboxFailureDecision.DeadLetter("sales-review");
    }

    public sealed class FailingPublisher : IMessagePublisher
    {
        internal Action? OnPublish { get; init; }

        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
        {
            OnPublish?.Invoke();
            throw new IOException("transport unavailable");
        }
    }
}
