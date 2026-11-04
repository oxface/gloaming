using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RelationalOwnershipConsumer;

namespace Gloaming.PersistenceTests;

public sealed class RelationalOwnershipTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static readonly string[] ExpectedColumns =
    [
        "Archived",
        "DefaultsJson",
        "Id",
        "Marker",
        "Name",
        "ReviewComment",
        "ReviewRevision",
        "ReviewSlug",
        "ReviewStatus",
        "ReviewSummary",
        "SelectionRevision",
        "SelectionSlug",
        "SnapshotJson",
        "TechnologyContext",
        "tenant_key",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeValueShapesRemainCompatibleWithOwnership(bool ownershipFirst) =>
        await ValueShapeJourney.RunAsync(
            await postgres.CreateDatabaseAsync(Token),
            ownershipFirst,
            Token
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRelationalValues_PreserveTrackingIsolationAndConcurrency(
        bool registerOwnershipFirst
    )
    {
        var connection = await postgres.CreateDatabaseAsync(Token);
        await RelationalOwnershipJourney.RunAsync(connection, registerOwnershipFirst, Token);
        await using var context = new RelationalDocumentsContext(
            new DbContextOptionsBuilder().UseNpgsql(connection).Options,
            "alpha"
        );
        var entity = Assert.Single(context.Model.GetEntityTypes());
        var table = Assert.Single(context.Model.GetRelationalModel().Tables);
        Assert.Equal("documents", table.Name);
        Assert.Equal("inline_consumer", table.Schema);
        Assert.True(entity.FindProperty(nameof(RelationalDocumentRow.Tenant))!.IsConcurrencyToken);

        await using var native = new NpgsqlConnection(connection);
        await native.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'inline_consumer' AND table_name = 'documents'
            ORDER BY column_name
            """,
            native
        );
        await using var reader = await command.ExecuteReaderAsync(Token);
        var columns = new Dictionary<string, (string Type, string Nullable, int? Length)>();
        while (await reader.ReadAsync(Token))
            columns.Add(
                reader.GetString(0),
                (
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3)
                )
            );

        Assert.Equal(
            ExpectedColumns.Order(StringComparer.Ordinal),
            columns.Keys.Order(StringComparer.Ordinal)
        );
        Assert.Equal(("character varying", "NO", (int?)200), columns["Name"]);
        Assert.Equal(("text", "YES", (int?)null), columns["TechnologyContext"]);
        Assert.Equal("YES", columns["SelectionSlug"].Nullable);
        Assert.Equal("YES", columns["ReviewSlug"].Nullable);
        Assert.Equal("NO", columns["ReviewSummary"].Nullable);
        Assert.Equal("jsonb", columns["SnapshotJson"].Type);
        Assert.Equal("jsonb", columns["DefaultsJson"].Type);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
