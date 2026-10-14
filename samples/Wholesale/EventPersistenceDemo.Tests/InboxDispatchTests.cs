using System.Diagnostics;
using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo.Tests;

public sealed class InboxDispatchTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ForwardMigrationsPreserveStockStateAndLegacyPendingMessages()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using (var setup = Scope(provider, Alpha))
            await setup
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .GetService<IMigrator>()
                .MigrateAsync("20261008213945_AddDurableInboxAndMessageMetadata", Token);

        Guid stock = await SeedAsync(provider, Alpha);
        // Run the real historical stock decision, but capture its outgoing envelope for
        // native insertion into the old schema. Today's EF outbox model needs new columns.
        var captured = new HistoricalOutbox();
        var legacyServices = DemoComposition.CreateServices(connection);
        legacyServices.AddStockIssueInbox();
        legacyServices.AddSingleton<IOutbox<InventoryDbContext>>(captured);
        await using (var legacyProvider = legacyServices.BuildServiceProvider())
        await using (var direct = Scope(legacyProvider, Alpha))
        {
            var database = direct.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await direct
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .IssueAsync(new(stock, 2, [new StockIssue(1)]), Token)
            );
            var reply = Assert.Single(captured.Messages);
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO inventory.outbox_messages (message_id, destination, message_name, schema_version, payload,
                    owner_key, correlation_id, causation_id, attempts)
                VALUES ({reply.MessageId}, {reply.RouteKey}, {reply.MessageName}, {reply.SchemaVersion},
                    CAST({reply.Payload.GetRawText()} AS jsonb), {reply.TenantKey}, {reply.CorrelationId}, {reply.CausationId}, 0)
                """,
                Token
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        var request = Message(stock, 3, 2);
        await using (var historical = Scope(provider, Alpha))
        {
            var database = historical.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO inventory.inbox_messages (subscription_key, producer_key, message_id, message_name, schema_version, payload,
                    tenant_key, correlation_id, causation_id)
                VALUES ({StockIssueMessageAdmission.Subscription}, {request.ProducerKey}, {request.MessageId},
                    {request.MessageName}, {request.SchemaVersion}, CAST({request.Payload.GetRawText()} AS jsonb),
                    {request.TenantKey}, {request.CorrelationId}, {request.CausationId})
                """,
                Token
            );
        }

        await using var upgraded = Scope(provider, Alpha);
        var current = upgraded.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await current.Database.MigrateAsync(Token);
        Assert.Equal(
            current.Database.GetMigrations(),
            await current.Database.GetAppliedMigrationsAsync(Token)
        );
        Assert.False(current.Database.HasPendingModelChanges());
        Assert.Empty(await current.Set<AuditRecord>().ToArrayAsync(Token));
        Assert.Equal(
            3L,
            await current
                .Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM inventory.events")
                .SingleAsync(Token)
        );
        Assert.Single(await current.Set<OutboxMessageRecord>().AsNoTracking().ToArrayAsync(Token));
        var retained = await current.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(Token);
        Assert.Equal(request.MessageId, retained.MessageId);
        Assert.Null(retained.ProcessedAt);
        Assert.Null(retained.TraceParent);
        Assert.Null(retained.TraceState);
        var legacyOutgoing = await current
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(Token);
        Assert.Null(legacyOutgoing.TraceParent);
        Assert.Null(legacyOutgoing.TraceState);
        var before = await upgraded
            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
            .ReadCurrentAsync(stock, Token);
        Assert.Equal(3, before!.Version);
        Assert.Equal(4m, before.OnHand);

        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        var after = await upgraded
            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
            .ReadCurrentAsync(stock, Token);
        Assert.Equal(4, after!.Version);
        Assert.Equal(2m, after.OnHand);
        Assert.Single(await current.Set<AuditRecord>().ToArrayAsync(Token));
        Assert.Equal(2, await current.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.NotNull(
            (await current.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(Token)).ProcessedAt
        );
    }

    [Fact]
    public async Task AdmittedIssueLinksRetainedContextAndExplicitReplyCaptureUsesProcessingActivity()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        Guid stock = await SeedAsync(provider, Alpha);
        var original = Message(stock, 2, 1);
        const string upstream = "00-12345678901234567890123456789012-1234567890123456-01";
        var request = new IncomingMessage(
            original.MessageId,
            original.ProducerKey,
            original.MessageName,
            original.SchemaVersion,
            original.Payload,
            original.TenantKey,
            original.CorrelationId,
            original.CausationId,
            upstream,
            "proof=inventory"
        );
        await IntakeAsync(provider, request);
        var spans = new List<Activity>();
        using var telemetry = Sdk.CreateTracerProviderBuilder()
            .AddSource("Gloaming.Messaging")
            .AddInMemoryExporter(spans)
            .Build();
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        telemetry.ForceFlush();
        var processed = Assert.Single(
            spans,
            span => Equals(span.GetTagItem("messaging.message.id"), request.MessageId.ToString())
        );
        Assert.Equal(ActivityKind.Consumer, processed.Kind);
        Assert.Equal(
            ActivitySpanId.CreateFromString(upstream.AsSpan(36, 16)),
            Assert.Single(processed.Links).Context.SpanId
        );
        Assert.Equal("proof=inventory", Assert.Single(processed.Links).Context.TraceState);
        await using var read = Scope(provider, Alpha);
        var database = read.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var reply = await database.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(processed.Id, reply.TraceParent);
        Assert.Equal(request.CorrelationId, reply.CorrelationId);
        Assert.Equal(request.MessageId.ToString(), reply.CausationId);
        Assert.Equal(Alpha, reply.TenantKey);
        Assert.Single(await database.Set<AuditRecord>().ToArrayAsync(Token));
        var state = await read
            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
            .ReadCurrentAsync(stock, Token);
        Assert.Equal(3, state!.Version);
        Assert.Equal(4m, state.OnHand);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("shortage")]
    [InlineData("conflict")]
    [InlineData("missing")]
    public async Task AdmittedCommandAtomicallyCompletesWithStockFactsOrAnExplicitBusinessRefusal(
        string decision
    )
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        Guid stock = await SeedAsync(provider, Alpha);
        var request = Message(
            decision == "missing" ? Guid.NewGuid() : stock,
            decision == "conflict" ? 7 : 2,
            decision == "shortage" ? 6 : 2
        );
        await IntakeAsync(provider, request);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var read = Scope(provider, Alpha);
        var database = read.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var state = await read
            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
            .ReadCurrentAsync(stock, Token);
        Assert.NotNull(state);
        bool accepted = decision == "accepted";
        var audits = await database.Set<AuditRecord>().ToArrayAsync(Token);
        if (accepted)
        {
            var audit = Assert.Single(audits);
            Assert.Equal(ActorKind.System, audit.ActorKind);
            Assert.Equal("inventory.stock-issue-worker", audit.ActorKey);
            Assert.Null(audit.InitiatorKind);
            Assert.Null(audit.InitiatorKey);
            Assert.Equal(Alpha, audit.TenantKey);
            Assert.Equal("inventory", audit.Source);
            Assert.Equal("stock-position.issued", audit.Action);
            Assert.Equal("stock-position", audit.SubjectType);
            Assert.Equal(stock.ToString("D"), audit.SubjectKey);
            Assert.Equal("accepted", audit.Outcome);
            Assert.Equal(1, audit.SchemaVersion);
            Assert.Null(audit.ReasonCode);
            Assert.Equal(3, audit.Details.GetProperty("Version").GetInt64());
            Assert.Equal(2m, audit.Details.GetProperty("Quantity").GetDecimal());
        }
        else
            Assert.Empty(audits);

        Assert.Equal(accepted ? 3 : 2, state.Version);
        Assert.Equal(accepted ? 3m : 5m, state.OnHand);
        Assert.NotNull((await database.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        var reply = Assert.Single(await database.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        Assert.Equal(Alpha, reply.TenantKey);
        Assert.Equal(request.CorrelationId, reply.CorrelationId);
        Assert.Equal(request.MessageId.ToString(), reply.CausationId);
        Assert.Equal(
            accepted ? "inventory.stock-issue-recorded" : "inventory.stock-issue-declined",
            reply.MessageName
        );
        if (!accepted)
            Assert.Equal(
                decision switch
                {
                    "shortage" => (int)StockIssueDeclineReason.InsufficientStock,
                    "conflict" => (int)StockIssueDeclineReason.Conflict,
                    _ => (int)StockIssueDeclineReason.NotFound,
                },
                reply.Payload.GetProperty("reason").GetInt32()
            );
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await IntakeAsync(provider, request));
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
        Assert.Equal(accepted ? 1 : 0, await database.Set<AuditRecord>().CountAsync(Token));
        Assert.False(database.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("event")]
    [InlineData("header")]
    [InlineData("state")]
    [InlineData("audit")]
    [InlineData("reply")]
    [InlineData("completion")]
    public async Task FailureAfterAcceptedDecisionRollsBackFactsInlineStateAuditReplyAndCompletionThenFreshScopeRecovers(
        string fault
    )
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        Guid stock = await SeedAsync(provider, Alpha);
        await IntakeAsync(provider, Message(stock, 2, 2));
        var (table, predicate) = fault switch
        {
            "event" => ("events", "stream_version <= 2"),
            "header" => ("event_streams", "version <= 2"),
            "state" => ("stock_position_current", "version <= 2"),
            "audit" => ("audit_entries", "schema_version > 1"),
            "reply" => ("outbox_messages", "schema_version > 1"),
            "completion" => ("inbox_messages", "processed_at IS NULL"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        await SqlAsync(
            connection,
            $"ALTER TABLE inventory.{table} ADD CONSTRAINT proof_participant CHECK ({predicate})"
        );
        await Assert.ThrowsAnyAsync<Exception>(() => ProcessAsync(provider));
        await using (var read = Scope(provider, Alpha))
        {
            var database = read.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var state = await read
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(stock, Token);
            Assert.Equal(2, state!.Version);
            Assert.Equal(5m, state.OnHand);
            Assert.Empty(await database.Set<OutboxMessageRecord>().ToArrayAsync(Token));
            Assert.Empty(await database.Set<AuditRecord>().ToArrayAsync(Token));
            Assert.Equal(
                2L,
                await database
                    .Database.SqlQueryRaw<long>(
                        "SELECT count(*) AS \"Value\" FROM inventory.events"
                    )
                    .SingleAsync(Token)
            );
            Assert.Equal(
                2L,
                await database
                    .Database.SqlQueryRaw<long>(
                        "SELECT version AS \"Value\" FROM inventory.event_streams"
                    )
                    .SingleAsync(Token)
            );
            Assert.Null((await database.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        }
        await SqlAsync(
            connection,
            $"ALTER TABLE inventory.{table} DROP CONSTRAINT proof_participant"
        );
        await SqlAsync(
            connection,
            "UPDATE inventory.inbox_messages SET available_at = clock_timestamp() - interval '1 second'"
        );
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var recovered = Scope(provider, Alpha);
        var current = await recovered
            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
            .ReadCurrentAsync(stock, Token);
        Assert.Equal(3, current!.Version);
        Assert.Equal(3m, current.OnHand);
        var recoveredDatabase = recovered.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Single(await recoveredDatabase.Set<AuditRecord>().ToArrayAsync(Token));
        Assert.Equal(
            3L,
            await recoveredDatabase
                .Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM inventory.events")
                .SingleAsync(Token)
        );
        Assert.NotNull(
            (await recoveredDatabase.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt
        );
        Assert.Single(
            await recovered
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Set<OutboxMessageRecord>()
                .ToArrayAsync(Token)
        );
    }

    [Fact]
    public async Task FreshProcessingScopesKeepTwoAdmittedOrganizationsIndependentAndRejectUnknownMetadata()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        Guid alpha = await SeedAsync(provider, Alpha);
        Guid beta = await SeedAsync(provider, Beta);
        await IntakeAsync(provider, Message(alpha, 2, 1));
        await IntakeAsync(provider, Message(beta, 2, 2, Beta));
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        foreach (string owner in new[] { Alpha, Beta })
        {
            await using var scope = Scope(provider, owner);
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal(owner, (await database.Set<AuditRecord>().SingleAsync(Token)).TenantKey);
            Assert.Equal(
                owner,
                (await database.Set<InboxMessageRecord>().SingleAsync(Token)).TenantKey
            );
            Assert.Equal(
                owner,
                (await database.Set<OutboxMessageRecord>().SingleAsync(Token)).TenantKey
            );
        }
        var denied = Message(alpha, 3, 1, "unadmitted");
        Assert.Throws<InvalidDataException>(() => StockIssueMessageAdmission.Validate(denied));
        // Even incorrectly retained technical intake cannot grant business admission during processing.
        await IntakeAsync(provider, denied);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcessAsync(provider));
        await using var read = Scope(provider, Alpha);
        Assert.Equal(
            4m,
            (
                await read
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadCurrentAsync(alpha, Token)
            )!.OnHand
        );
    }

    [Fact]
    public async Task NativeBrokerJourneyExercisesTheOptInInventoryReceiver()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        using var output = new StringWriter();
        await InboxJourney.RunAsync(connection, new Uri(rabbit.ConnectionString), output, Token);
        Assert.Contains("processing=Processed", output.ToString());
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var incoming = await database.Set<InboxMessageRecord>().SingleAsync(Token);
        var reply = await database.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.NotNull(incoming.ProcessedAt);
        Assert.Equal(incoming.MessageId.ToString(), reply.CausationId);
        Assert.Equal(incoming.CorrelationId, reply.CorrelationId);
        var audit = await database.Set<AuditRecord>().SingleAsync(Token);
        Assert.Equal("stock-position", audit.SubjectType);
        Assert.NotNull(audit.SubjectKey);
        Assert.Contains("audit=stock-position.issued", output.ToString());
        Assert.Contains(
            $"reply={reply.MessageId} reply-cause={incoming.MessageId}",
            output.ToString()
        );
    }

    [Fact]
    public async Task CommittedAcceptedProcessingRequiresItsMatchingAuditAndReply()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        Guid stock = await SeedAsync(provider, Alpha);
        var request = Message(stock, 2, 2);
        await IntakeAsync(provider, request);
        await SqlAsync(
            connection,
            """
            CREATE FUNCTION inventory.proof_required_issue_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.processed_at IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM inventory.audit_entries a
                    JOIN inventory.outbox_messages o ON o.causation_id = NEW.message_id::text
                    WHERE a.tenant_key = NEW.tenant_key
                      AND a.source = 'inventory' AND a.action = 'stock-position.issued'
                      AND a.outcome = 'accepted' AND a.actor_kind = 3
                      AND a.actor_key = 'inventory.stock-issue-worker'
                      AND a.subject_key = NEW.payload->>'stockPositionId'
                      AND (a.details->>'Version')::bigint = (NEW.payload->>'expectedVersion')::bigint + 1
                      AND (a.details->>'Quantity')::numeric = (NEW.payload->>'quantity')::numeric
                      AND o.message_name = 'inventory.stock-issue-recorded'
                      AND o.owner_key = NEW.tenant_key
                      AND o.payload->>'stockPositionId' = a.subject_key
                      AND (o.payload->>'version')::bigint = (a.details->>'Version')::bigint
                ) THEN RAISE EXCEPTION 'completed accepted issue without expected audit/reply'; END IF;
                RETURN NULL;
            END; $$;
            CREATE CONSTRAINT TRIGGER proof_issue_audit AFTER UPDATE ON inventory.inbox_messages
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION inventory.proof_required_issue_audit();
            """
        );
        // Negative control: native completion without participants cannot satisfy this oracle.
        var missingAudit = await Assert.ThrowsAsync<PostgresException>(() =>
            SqlAsync(
                connection,
                "UPDATE inventory.inbox_messages SET processed_at = clock_timestamp()"
            )
        );
        Assert.Equal(
            "completed accepted issue without expected audit/reply",
            missingAudit.MessageText
        );
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var read = Scope(provider, Alpha);
        var database = read.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Single(await database.Set<AuditRecord>().ToArrayAsync(Token));
        Assert.NotNull((await database.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
    }

    private sealed class HistoricalOutbox : IOutbox<InventoryDbContext>
    {
        internal List<OutgoingMessage> Messages { get; } = [];

        public void Enqueue(OutgoingMessage message) => Messages.Add(message);
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        await scope
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Database.MigrateAsync(Token);
        return connection;
    }

    private static ServiceProvider Provider(string connection)
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddStockIssueInbox();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    private static IncomingMessage Message(
        Guid stock,
        long version,
        decimal quantity,
        string owner = Alpha,
        string? correlationId = "issue-conversation",
        string? causationId = "earlier-request"
    ) =>
        new(
            Guid.NewGuid(),
            StockIssueMessageAdmission.Producer,
            StockIssueMessageAdmission.Subscription,
            1,
            JsonSerializer.SerializeToElement(new IssueStockV1(stock, version, quantity), WireJson),
            owner,
            correlationId,
            causationId
        );

    private static AsyncServiceScope Scope(ServiceProvider provider, string owner)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(owner)));
        return scope;
    }

    private static async Task<Guid> SeedAsync(ServiceProvider provider, string owner)
    {
        Guid stock = Guid.NewGuid();
        await using (var scope = Scope(provider, owner))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .OpenAsync(new(stock, Guid.NewGuid(), Guid.NewGuid(), "EA"), Token)
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using (var scope = Scope(provider, owner))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .ReceiveAsync(new(stock, 1, [new StockReceipt(5)]), Token)
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        return stock;
    }

    private static async Task<InboxReceiveResult> IntakeAsync(
        ServiceProvider provider,
        IncomingMessage message
    )
    {
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var result = await scope
            .ServiceProvider.GetRequiredService<IInbox<InventoryDbContext>>()
            .ReceiveAsync(StockIssueMessageAdmission.Subscription, message, Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static async Task<InboxProcessingResult> ProcessAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<InventoryDbContext>>()
            .ProcessNextAsync(StockIssueMessageAdmission.Subscription, Token);
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }
}
