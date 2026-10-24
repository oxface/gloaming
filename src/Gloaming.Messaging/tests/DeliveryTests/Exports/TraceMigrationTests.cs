using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gloaming.Samples.Messaging.Tests.Exports;

public sealed class TraceMigrationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task UpgradePreservesHistoricalPendingWorkAndDispatchesWithoutTraceContext()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        Guid message = Guid.NewGuid();
        const string payload = "{\"pages\":3}";
        await using (var legacy = ExportDbContext.Create(connection))
        {
            await legacy
                .GetService<IMigrator>()
                .MigrateAsync("20261008213949_AddMessageMetadata", token);
            // Populate the historical schema natively, without pretending today's EF model
            // can enqueue against columns that have not been migrated yet.
            await legacy.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO exports.outgoing_work (message_id, destination, message_name, schema_version, payload, attempts)
                VALUES ({message}, 'exports.render', 'exports.render', 1, CAST({payload} AS jsonb), 0)
                """,
                token
            );
            await legacy.Database.MigrateAsync(token);
            Assert.False(legacy.Database.HasPendingModelChanges());
            var row = await legacy.Set<OutboxMessageRecord>().SingleAsync(token);
            Assert.Equal(message, row.MessageId);
            Assert.Null(row.TraceParent);
            Assert.Null(row.TraceState);
        }

        var publisher = new LegacyPublisher();
        await using var fresh = ExportDbContext.Create(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await new PostgresOutboxDispatcher<ExportDbContext>(
                fresh,
                publisher,
                new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
            ).DispatchNextAsync(token)
        );
        Assert.Equal(message, publisher.Message!.MessageId);
        Assert.Null(publisher.Message.TraceParent);
        Assert.NotNull((await fresh.Set<OutboxMessageRecord>().SingleAsync(token)).DispatchedAt);
    }

    private sealed class LegacyPublisher : IMessagePublisher
    {
        internal OutgoingMessage? Message { get; private set; }

        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }
}
