using Npgsql;
using Testcontainers.PostgreSql;

namespace Gloaming.Tests.Infrastructure;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        // Only a locally generated identifier is interpolated; no consumer input or credentials.
        string database = "proof_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = database,
            // Each proof gets a new database. Retaining its idle pool exhausts the shared container.
            Pooling = false,
        }.ConnectionString;
    }
}
