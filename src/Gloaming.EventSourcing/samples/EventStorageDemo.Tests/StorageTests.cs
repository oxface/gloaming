using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.EventStorageDemo.Tests;

public sealed class StorageTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NativeExecutableStoresTwoFamiliesInTheSameCustomTablePair()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(DemoJourneys).Assembly.Location);
        start.Environment["EVENT_STORAGE_DEMO_CONNECTION_STRING"] = connection;
        using var process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
        Task<string> error = process.StandardError.ReadToEndAsync(Token);
        await process.WaitForExitAsync(Token);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await error);
        Assert.Equal(
            [
                "journal proof.counter: version=2, value=17",
                "journal proof.note: version=1, text=review",
                "journal append: version=3, value=22, rejected=4",
            ],
            (await output).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        );

        await using var read = new StorageDbContext(StorageDbContext.Options(connection));
        Assert.Equal(
            ["proof.counter", "proof.counter", "proof.note"],
            await read
                .Streams.AsNoTracking()
                .OrderBy(row => row.StreamType)
                .Select(row => row.StreamType)
                .ToArrayAsync(Token)
        );
        Assert.Equal(
            "Independent counter",
            (
                await read
                    .Streams.AsNoTracking()
                    .SingleAsync(row => row.Id == DemoData.CounterId, Token)
            ).Description
        );
        Assert.Equal(6, await read.Events.CountAsync(Token));
        Assert.Equal(
            2,
            await read.Events.CountAsync(row => row.StreamId == DemoData.CounterId, Token)
        );
        Assert.Empty(await read.Database.GetPendingMigrationsAsync(Token));
        await using var native = new NpgsqlConnection(connection);
        await native.OpenAsync(Token);
        await using var tables = new NpgsqlCommand(
            "SELECT array_agg(table_name::text ORDER BY table_name) FROM information_schema.tables WHERE table_schema = 'journal' AND table_name <> '__EFMigrationsHistory'",
            native
        );
        Assert.Equal(["facts", "streams"], (string[])(await tables.ExecuteScalarAsync(Token))!);
    }

    [Theory]
    [InlineData("stream-identity", PostgresErrorCodes.UniqueViolation, "PK_streams")]
    [InlineData(
        "position",
        PostgresErrorCodes.UniqueViolation,
        "IX_facts_stream_id_stream_version"
    )]
    [InlineData(
        "missing-reference",
        PostgresErrorCodes.ForeignKeyViolation,
        "FK_facts_streams_stream_id"
    )]
    [InlineData(
        "delete-header",
        PostgresErrorCodes.RestrictViolation,
        "FK_facts_streams_stream_id"
    )]
    public async Task SelectedIdentityAndRelationshipAreEnforcedInPostgreSql(
        string fault,
        string state,
        string constraint
    )
    {
        string connection = await SeedAsync();
        await using (var write = new StorageDbContext(StorageDbContext.Options(connection)))
        {
            if (fault == "stream-identity")
                write.Streams.Add(
                    new EventStreamRecord
                    {
                        Id = DemoData.CounterId,
                        StreamType = "proof.note",
                        Version = 1,
                        CreatedAt = DemoData.RecordedAt,
                        UpdatedAt = DemoData.RecordedAt,
                    }
                );
            else if (fault == "delete-header")
                write.Streams.Remove(
                    await write.Streams.SingleAsync(row => row.Id == DemoData.CounterId, Token)
                );
            else
                write.Events.Add(
                    DemoData.Event(
                        fault == "position" ? DemoData.CounterId : Guid.NewGuid(),
                        2,
                        "proof.other-fact",
                        JsonSerializer.SerializeToElement(new { amount = 99 })
                    )
                );
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                write.SaveChangesAsync(Token)
            );
            var databaseFailure = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(state, databaseFailure.SqlState);
            Assert.Equal(constraint, databaseFailure.ConstraintName);
        }
        await using var read = new StorageDbContext(StorageDbContext.Options(connection));
        var counter = await read
            .Streams.AsNoTracking()
            .SingleAsync(row => row.Id == DemoData.CounterId, Token);
        Assert.Equal("proof.counter", counter.StreamType);
        Assert.Equal(2, counter.Version);
        Assert.Equal(3, await read.Events.CountAsync(Token));
    }

    [Fact]
    public async Task ConfiguredHeaderVersionRejectsAStaleNativeWriter()
    {
        string connection = await SeedAsync();
        var options = StorageDbContext.Options(connection);
        await using var winner = new StorageDbContext(options);
        await using var loser = new StorageDbContext(options);
        var winnerHeader = await winner.Streams.SingleAsync(
            row => row.Id == DemoData.CounterId,
            Token
        );
        var loserHeader = await loser.Streams.SingleAsync(
            row => row.Id == DemoData.CounterId,
            Token
        );
        winnerHeader.Version = 3;
        loserHeader.Version = 4;
        winner.Events.Add(
            DemoData.Event(
                DemoData.CounterId,
                3,
                "proof.counter-increased",
                JsonSerializer.SerializeToElement(new { amount = 4 })
            )
        );
        await using (var transaction = await winner.Database.BeginTransactionAsync(Token))
        {
            await winner.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync(Token));
        await using var read = new StorageDbContext(options);
        Assert.Equal(
            3,
            (
                await read
                    .Streams.AsNoTracking()
                    .SingleAsync(row => row.Id == DemoData.CounterId, Token)
            ).Version
        );
        Assert.Equal(4, await read.Events.CountAsync(Token));
    }

    private async Task<string> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var setup = new StorageDbContext(StorageDbContext.Options(connection));
        await setup.Database.MigrateAsync(Token);
        DemoData.Stage(setup);
        await setup.SaveChangesAsync(Token);
        return connection;
    }
}
