using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed partial class RetentionTests
{
    [Fact]
    public async Task HiddenConflictingReceiptFailsBoundedlyWithoutInventingAContentConflictOrAcceptance()
    {
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        await using (var owner = Provider(connection))
            await ReceiveAsync(owner, message);

        await SqlAsync(connection, "UPDATE receiver.inbox SET tenant_key = 'beta'");
        string role = "inbox_hidden_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD 'proof' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA receiver TO "{role}";
            GRANT SELECT, INSERT ON receiver.inbox TO "{role}";
            ALTER TABLE receiver.inbox ENABLE ROW LEVEL SECURITY;
            CREATE POLICY hidden_select ON receiver.inbox FOR SELECT TO "{role}" USING (tenant_key = 'alpha');
            CREATE POLICY admitted_insert ON receiver.inbox FOR INSERT TO "{role}" WITH CHECK (true);
            """
        );
        await using var provider = Provider(RoleConnection(connection, role));
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>()
                .ReceiveAsync("render", message, deadline.Token)
        );
        await transaction.RollbackAsync(Token);
        await using var fresh = Context(connection);
        Assert.Single(await fresh.Set<InboxMessageRecord>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task MetadataOnlyMaintenanceRespectsNativeCompletedLaneTenantPoliciesDespiteEfFilterBypass()
    {
        string connection = await DatabaseAsync(postgres);
        await using var intake = Provider(connection);
        for (int i = 0; i < 4; i++)
            await ReceiveAsync(intake, Message());

        await SqlAsync(
            connection,
            """
            UPDATE receiver.inbox SET processed_at = '2020-01-01Z';
            UPDATE receiver.inbox SET tenant_key = 'beta' WHERE message_id = (SELECT message_id FROM receiver.inbox ORDER BY message_id LIMIT 1);
            UPDATE receiver.inbox SET producer_key = 'other' WHERE message_id = (SELECT message_id FROM receiver.inbox ORDER BY message_id OFFSET 1 LIMIT 1);
            UPDATE receiver.inbox SET processed_at = NULL WHERE message_id = (SELECT message_id FROM receiver.inbox ORDER BY message_id OFFSET 2 LIMIT 1);
            """
        );
        string role = "inbox_maintenance_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD 'proof' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT USAGE ON SCHEMA receiver TO "{role}";
            GRANT SELECT(subscription_key, producer_key, message_id, receipt_id, processed_at, quarantined_at), DELETE
                ON receiver.inbox TO "{role}";
            ALTER TABLE receiver.inbox ENABLE ROW LEVEL SECURITY;
            ALTER TABLE receiver.inbox FORCE ROW LEVEL SECURITY;
            CREATE POLICY maintenance_select ON receiver.inbox FOR SELECT TO "{role}"
                USING (tenant_key = 'alpha' AND subscription_key = 'render' AND producer_key = 'exports'
                    AND processed_at IS NOT NULL AND quarantined_at IS NULL);
            CREATE POLICY maintenance_delete ON receiver.inbox FOR DELETE TO "{role}"
                USING (tenant_key = 'alpha' AND subscription_key = 'render' AND producer_key = 'exports'
                    AND processed_at IS NOT NULL AND quarantined_at IS NULL);
            """
        );
        string runtime = RoleConnection(connection, role);
        await using var database = new FilteredInbox(
            new DbContextOptionsBuilder<FilteredInbox>().UseNpgsql(runtime).Options
        );
        // This consumer filter hides the lane. Raw maintenance deliberately relies on native authority instead.
        Assert.Equal(0, await database.Set<InboxMessageRecord>().CountAsync(Token));
        Assert.Equal(
            1,
            await database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                Token
            )
        );
        Assert.Equal(
            0,
            await database.Database.ExecuteSqlRawAsync("DELETE FROM receiver.inbox", Token)
        );
        foreach (
            string forbidden in new[]
            {
                "SELECT payload FROM receiver.inbox",
                "SELECT * FROM receiver.handled",
                "UPDATE receiver.inbox SET revision = revision + 1",
                "INSERT INTO receiver.inbox DEFAULT VALUES",
            }
        )
            Assert.Equal(
                PostgresErrorCodes.InsufficientPrivilege,
                (
                    await Assert.ThrowsAsync<PostgresException>(() => SqlAsync(runtime, forbidden))
                ).SqlState
            );

        await using var fresh = Context(connection);
        Assert.Equal(3, await fresh.Set<InboxMessageRecord>().CountAsync(Token));
        Assert.Single(
            await fresh
                .Set<InboxMessageRecord>()
                .Where(row => row.ProcessedAt == null)
                .ToArrayAsync(Token)
        );
    }

    [Theory]
    [InlineData("intake")]
    [InlineData("processor")]
    [InlineData("operator")]
    public async Task DeliveryRolesHaveNoCleanupAuthority(string kind)
    {
        string connection = await DatabaseAsync(postgres);
        string role = "inbox_" + Guid.NewGuid().ToString("N");
        string grant = kind switch
        {
            "intake" => "INSERT",
            "processor" =>
                "UPDATE(processed_at, available_at, revision, failure_count, failures_since_redrive, last_failed_at, last_failure_code, quarantined_at)",
            _ =>
                "UPDATE(available_at, revision, quarantined_at, failures_since_redrive, redrive_count, last_redriven_at)",
        };
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD 'proof' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA receiver TO "{role}";
            GRANT SELECT, {grant} ON receiver.inbox TO "{role}";
            """
        );
        string runtime = RoleConnection(connection, role);
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            (
                await Assert.ThrowsAsync<PostgresException>(() =>
                    SqlAsync(runtime, "DELETE FROM receiver.inbox")
                )
            ).SqlState
        );
        if (kind == "intake")
        {
            await using var provider = Provider(runtime);
            var message = Message();
            Assert.Equal(InboxReceiveResult.Queued, await ReceiveAsync(provider, message));
            Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(provider, message));
        }
    }

    [Fact]
    public async Task InspectorNeedsReceiptGenerationReadButNeverPayloadOrUpdateAuthority()
    {
        string connection = await DatabaseAsync(postgres);
        await using (var intake = Provider(connection))
            await ReceiveAsync(intake, Message());

        await SqlAsync(
            connection,
            """
            UPDATE receiver.inbox SET revision = 1, failure_count = 1, failures_since_redrive = 1,
                last_failed_at = clock_timestamp(), last_failure_code = 'review', quarantined_at = clock_timestamp();
            """
        );
        string role = "inbox_inspector_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD 'proof' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA receiver TO "{role}";
            GRANT SELECT(subscription_key, producer_key, message_id, message_name, schema_version, revision,
                failure_count, failures_since_redrive, received_at, quarantined_at, last_failure_code,
                last_failed_at, redrive_count, last_redriven_at, processed_at) ON receiver.inbox TO "{role}";
            """
        );
        var services = Services(RoleConnection(connection, role));
        services.AddPostgresInboxDeadLetters<InboxConsumer>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            (
                await Assert.ThrowsAsync<PostgresException>(() =>
                    letters.ListAsync("render", "exports", new(), Token)
                )
            ).SqlState
        );
        await SqlAsync(connection, $"GRANT SELECT(receipt_id) ON receiver.inbox TO \"{role}\"");
        var item = Assert.Single(
            (await letters.ListAsync("render", "exports", new(), Token)).Items
        );
        Assert.NotEqual(Guid.Empty, item.ReceiptId);
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            (
                await Assert.ThrowsAsync<PostgresException>(() =>
                    letters.RetryAsync(
                        "render",
                        "exports",
                        item.MessageId,
                        item.ReceiptId,
                        item.Revision,
                        Token
                    )
                )
            ).SqlState
        );
    }

    private static string RoleConnection(string connection, string role) =>
        new NpgsqlConnectionStringBuilder(connection)
        {
            Username = role,
            Password = "proof",
        }.ConnectionString;

    public sealed class FilteredInbox(DbContextOptions<FilteredInbox> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder
                .ConfigurePostgresInbox("receiver", "inbox")
                .HasQueryFilter(row => row.SubscriptionKey == "hidden");
    }
}
