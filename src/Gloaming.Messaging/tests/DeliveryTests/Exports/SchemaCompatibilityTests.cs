using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Messaging.Tests.Exports;

public sealed class SchemaCompatibilityTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task V2RollbackPreservesPreviouslyQueuedV1AndTheUnsubmittedDraft()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        Guid draft = Guid.NewGuid(),
            oldMessage = Guid.NewGuid();
        using var historical = JsonDocument.Parse(
            """
            {"ExportRequestId":"bfc66c74-d508-47d6-9dcf-cbcd5a4b3d39","Pages":7}
            """
        );
        await using (var setup = ExportDbContext.Create(connection))
        {
            await setup.Database.MigrateAsync(Token);
            await using var transaction = await setup.Database.BeginTransactionAsync(Token);
            setup.Add(ExportRequest.Create(draft, 3));
            new EfOutbox<ExportDbContext>(setup).Enqueue(
                new OutgoingMessage(
                    oldMessage,
                    "exports.render",
                    "exports.render",
                    1,
                    historical.RootElement,
                    correlationId: "historical"
                )
            );
            await setup.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var writer = ExportDbContext.Create(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                ExportSubmissionResult.Accepted,
                await new ExportRequestCommands(
                    writer,
                    new EfOutbox<ExportDbContext>(writer),
                    renderSchemaVersion: 2
                ).SubmitAsync(draft, 1, Token)
            );
            await writer.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        await using var read = ExportDbContext.Create(connection);
        var row = await read.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(oldMessage, row.MessageId);
        Assert.Equal(1, row.SchemaVersion);
        Assert.Equal("historical", row.CorrelationId);
        Assert.True(JsonElement.DeepEquals(historical.RootElement, row.Payload));
        Assert.Null(row.DispatchedAt);
        var request = await read.Set<ExportRequest>().SingleAsync(Token);
        Assert.Equal(1, request.Version);
        Assert.False(request.Submitted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void InvalidWriterSelectionRejectsBeforeOpeningStorage(int schema)
    {
        using var database = ExportDbContext.Create(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=unused;Timeout=1"
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExportRequestCommands(database, new EfOutbox<ExportDbContext>(database), schema)
        );
        Assert.False(database.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(1, "Pages", "PageCount")]
    [InlineData(2, "PageCount", "Pages")]
    public async Task SelectedWriterSchemaCommitsItsOriginalWireShape(
        int schema,
        string field,
        string absentField
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        Guid id = Guid.NewGuid();
        await using (var writer = ExportDbContext.Create(connection))
        {
            await writer.Database.MigrateAsync(Token);
            writer.Add(ExportRequest.Create(id, 3));
            await writer.SaveChangesAsync(Token);
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            var commands = new ExportRequestCommands(
                writer,
                new EfOutbox<ExportDbContext>(writer),
                schema
            );
            Assert.Equal(ExportSubmissionResult.Accepted, await commands.SubmitAsync(id, 1, Token));
            await writer.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var read = ExportDbContext.Create(connection);
        var row = await read.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Equal("exports.render", row.MessageName);
        Assert.Equal("exports.render", row.RouteKey);
        Assert.Equal(schema, row.SchemaVersion);
        Assert.Equal(id, row.Payload.GetProperty("ExportRequestId").GetGuid());
        Assert.Equal(3, row.Payload.GetProperty(field).GetInt32());
        Assert.False(row.Payload.TryGetProperty(absentField, out _));
        Assert.Equal(2, (await read.Set<ExportRequest>().SingleAsync(Token)).Version);
        Assert.False(read.Database.HasPendingModelChanges());
    }
}
