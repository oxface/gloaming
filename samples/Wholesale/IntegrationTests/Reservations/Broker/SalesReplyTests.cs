using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed partial class SalesReplyTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    [Fact(Timeout = 60_000)]
    public async Task AdmittedReplyCommitsProgressNextCommandAndCompletionForOnlyItsTenant()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var alpha = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        var beta = await proof.Reservations.StartAsync(
            SalesReplyProof.Request(),
            token,
            WorkflowProof.Beta
        );
        await proof.IntakeAsync(await proof.ReplyAsync(alpha.OrderId, token), token);
        await proof.IntakeAsync(
            await proof.ReplyAsync(beta.OrderId, token, WorkflowProof.Beta),
            token
        );
        await using var services = proof.Services();
        await using (var scope = services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Current;
            var actor = scope.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current;
            Assert.Equal(WorkflowProof.Alpha, tenant.RequireTenant().Value);
            Assert.Equal(
                Actor.System(new ActorId(StockReservationReplyAdmission.WorkerActor)),
                actor.Actor
            );
            Assert.Equal(
                InboxProcessingResult.Processed,
                await scope
                    .ServiceProvider.GetRequiredService<IInboxProcessor<SalesDbContext>>()
                    .ProcessNextAsync(StockReservationReplyAdmission.Subscription, token)
            );
            Assert.Same(
                tenant,
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Current
            );
            Assert.Same(
                actor,
                scope.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current
            );
            Assert.Throws<InvalidOperationException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                    .Initialize(TenantContext.ForTenant(new TenantId(WorkflowProof.Beta)))
            );
        }

        Assert.Equal(
            StockReservationOrderStatus.AwaitingSecond,
            (await proof.Reservations.ReadAsync(alpha.OrderId, token))!.Status
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.Reservations.ReadAsync(beta.OrderId, token, WorkflowProof.Beta))!.Status
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
        Assert.Equal(
            3,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            InboxProcessingResult.NoWork,
            await SalesReplyProof.ProcessAsync(services, token)
        );
        await using var betaServices = proof.Services(WorkflowProof.Beta);
        Assert.Equal(
            InboxProcessingResult.Processed,
            await SalesReplyProof.ProcessAsync(betaServices, token)
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingSecond,
            (await proof.Reservations.ReadAsync(beta.OrderId, token, WorkflowProof.Beta))!.Status
        );
        Assert.Equal(
            4,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
    }

    [Theory(Timeout = 60_000)]
    [InlineData("workflow")]
    [InlineData("outbox")]
    [InlineData("completion")]
    public async Task FailedParticipantRollsBackAllEffectsAndFreshScopeRetries(string participant)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        await proof.IntakeAsync(await proof.ReplyAsync(started.OrderId, token), token);
        string table = participant == "workflow" ? "workflow_instances" : "outbox_messages";
        await proof.SqlAsync(
            participant == "completion"
                ? """
                CREATE FUNCTION public.reject_reply_completion() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'completion rejected' USING ERRCODE = '23514'; END; $$;
                CREATE TRIGGER reject_reply_completion BEFORE UPDATE OF processed_at ON sales.inbox_messages
                    FOR EACH ROW EXECUTE FUNCTION public.reject_reply_completion();
                """
                : $"ALTER TABLE sales.{table} ADD CONSTRAINT reject_reply CHECK (false) NOT VALID",
            token
        );
        await using var services = proof.Services();
        Exception? failure = await Record.ExceptionAsync(() =>
            SalesReplyProof.ProcessAsync(services, token)
        );
        if (participant == "completion")
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(failure).SqlState
            );
        else
            Assert.IsType<DbUpdateException>(failure);

        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.Reservations.ReadAsync(started.OrderId, token))!.Status
        );
        Assert.Equal(
            1,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NULL AND available_at > received_at",
                token
            )
        );
        await using (var connection = new NpgsqlConnection(proof.Runtime))
        {
            await connection.OpenAsync(token);
            await using var baseline = new NpgsqlCommand(
                "SELECT NULLIF(current_setting('app.tenant_key', true), '') IS NULL",
                connection
            );
            Assert.Equal(true, await baseline.ExecuteScalarAsync(token));
        }

        await proof.SqlAsync(
            participant == "completion"
                ? "DROP TRIGGER reject_reply_completion ON sales.inbox_messages"
                : $"ALTER TABLE sales.{table} DROP CONSTRAINT reject_reply",
            token
        );
        await proof.SqlAsync(
            "UPDATE sales.inbox_messages SET available_at = clock_timestamp() - interval '1 second'",
            token
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await SalesReplyProof.ProcessAsync(services, token)
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingSecond,
            (await proof.Reservations.ReadAsync(started.OrderId, token))!.Status
        );
        Assert.Equal(
            2,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
    }

    [Theory(Timeout = 60_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilterBypassAndNativeSqlRemainTenantScopedAndTransactionEndClearsContext(
        bool commit
    )
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var alpha = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        var beta = await proof.Reservations.StartAsync(
            SalesReplyProof.Request(),
            token,
            WorkflowProof.Beta
        );
        await proof.IntakeAsync(await proof.ReplyAsync(alpha.OrderId, token), token);
        await proof.IntakeAsync(
            await proof.ReplyAsync(beta.OrderId, token, WorkflowProof.Beta),
            token
        );
        await using var services = proof.Services();
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.Set<WorkflowInstanceRecord>().CountAsync(token)
            );
            await database.Database.OpenConnectionAsync(token);
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var rows = await database
                .Set<WorkflowInstanceRecord>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToArrayAsync(token);
            Assert.Equal(alpha.OrderId, Assert.Single(rows).Id);
            Assert.Equal(WorkflowProof.Alpha, Assert.Single(rows).TenantKey);
            Assert.Equal(
                1,
                await database.Set<InboxMessageRecord>().IgnoreQueryFilters().CountAsync(token)
            );
            Assert.Equal(
                0,
                await database
                    .Set<WorkflowInstanceRecord>()
                    .IgnoreQueryFilters()
                    .Where(row => row.Id == beta.OrderId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.Version, 999),
                        token
                    )
            );
            Assert.Equal(
                0,
                await database.Database.ExecuteSqlRawAsync(
                    "UPDATE sales.workflow_instances SET \"Version\" = 999 WHERE \"Id\" = {0}",
                    [beta.OrderId],
                    token
                )
            );
            if (commit)
                await transaction.CommitAsync(token);
            else
                await transaction.RollbackAsync(token);

            // Keep the same physical connection open: this is local-transaction cleanup, not a pool-reset assertion.
            await using var baseline = new NpgsqlCommand(
                "SELECT NULLIF(current_setting('app.tenant_key', true), '') IS NULL AND (SELECT count(*) FROM sales.workflow_instances) = 0 AND (SELECT count(*) FROM sales.inbox_messages) = 0",
                (NpgsqlConnection)database.Database.GetDbConnection()
            );
            Assert.Equal(true, await baseline.ExecuteScalarAsync(token));
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                database
                    .Set<WorkflowInstanceRecord>()
                    .IgnoreQueryFilters()
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.TenantKey, WorkflowProof.Beta),
                        token
                    )
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }

        Assert.Equal(
            1,
            (await proof.Reservations.ReadAsync(beta.OrderId, token, WorkflowProof.Beta))!.Version
        );
    }

    [Theory(Timeout = 60_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndTenantlessScopesFailBeforeProtectedTransactions(bool tenantless)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var tenant = new TenantContextAccessor();
            if (tenantless)
                tenant.Initialize(TenantContext.Tenantless());

            return tenant;
        });
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddSalesReservationReplyDatabase(proof.Runtime);
        services.AddAdmittedStockReservationReplies();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        Exception? failure = await Record.ExceptionAsync(() =>
            database.Database.BeginTransactionAsync(token)
        );
        if (tenantless)
            Assert.IsType<TenantRequiredException>(failure);
        else
            Assert.IsType<InvalidOperationException>(failure);

        Assert.Null(database.Database.CurrentTransaction);
    }

    [Theory(Timeout = 60_000)]
    [InlineData("foreign-tenant")]
    [InlineData("human-actor")]
    [InlineData("other-system")]
    public async Task AdmittedEntryRejectsForeignEnvelopeOrActorWithoutRebinding(string fault)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        string owner = fault == "foreign-tenant" ? WorkflowProof.Beta : WorkflowProof.Alpha;
        var started = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token, owner);
        var message = await proof.ReplyAsync(started.OrderId, token, owner);
        await using var services = proof.Services(configure: services =>
        {
            if (fault != "foreign-tenant")
                services.AddScoped(_ =>
                {
                    var actor = new ActorContextAccessor();
                    actor.Initialize(
                        new ActorContext(
                            fault == "human-actor"
                                ? Actor.Human(
                                    new ActorId(StockReservationReplyAdmission.WorkerActor)
                                )
                                : Actor.System(new ActorId("other.worker"))
                        )
                    );
                    return actor;
                });
        });
        await using var scope = services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Current;
        var actor = scope.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current;
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scope
                .ServiceProvider.GetRequiredKeyedService<IInboxHandler<SalesDbContext>>(
                    StockReservationReplyAdmission.Subscription
                )
                .HandleAsync(message, token)
        );
        Assert.Same(
            tenant,
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Current
        );
        Assert.Same(
            actor,
            scope.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current
        );
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Theory(Timeout = 60_000)]
    [InlineData(null, WorkflowProof.Alpha)]
    [InlineData(WorkflowProof.Alpha, WorkflowProof.Beta)]
    [InlineData(WorkflowProof.Alpha, WorkflowProof.Alpha)]
    public async Task NativeOutboxInsertRequiresMatchingTransactionTenant(
        string? databaseTenant,
        string owner
    )
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        await using var connection = new NpgsqlConnection(proof.Runtime);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (databaseTenant is not null)
        {
            await using var setup = new NpgsqlCommand(
                "SELECT set_config('app.tenant_key', @tenant, true)",
                connection,
                transaction
            );
            setup.Parameters.AddWithValue("tenant", databaseTenant);
            await setup.ExecuteScalarAsync(token);
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO sales.outbox_messages (message_id, destination, message_name, schema_version, payload, owner_key, attempts)
            VALUES (@id, 'inventory.reserve-stock', 'inventory.reserve-stock', 1, '{}'::jsonb, @owner, 0)
            """,
            connection,
            transaction
        );
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("owner", owner);
        if (databaseTenant == owner)
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(token));
        else
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                insert.ExecuteNonQueryAsync(token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
            Assert.Contains("row-level security", failure.MessageText, StringComparison.Ordinal);
        }
    }
}
