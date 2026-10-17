using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Tenancy.Postgres.Tests;

internal sealed class ProofDatabase(string admin, string role, string runtime) : IAsyncDisposable
{
    internal const string Setting = "consumer.partition_key";
    internal const string Alpha = "tenant:α/'\n$partition";
    internal const string Beta = "tenant:beta";

    internal NpgsqlDataSource Runtime { get; } = NpgsqlDataSource.Create(runtime);

    internal static async Task<ProofDatabase> CreateAsync(
        PostgreSqlFixture postgres,
        CancellationToken token
    )
    {
        string admin = await postgres.CreateDatabaseAsync(token);
        string role = "tenant_runtime_" + Guid.NewGuid().ToString("N");
        string password = Guid.NewGuid().ToString("N");
        var runtime = new NpgsqlConnectionStringBuilder(admin)
        {
            Username = role,
            Password = password,
            Pooling = true,
            MaxPoolSize = 1,
            Timeout = 10,
        };
        var proof = new ProofDatabase(admin, role, runtime.ConnectionString);
        try
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync(token);
            // Only generated role/password values are interpolated. Consumer tenant values are parameters.
            await using var setup = new NpgsqlCommand(
                $"""
                CREATE ROLE "{role}" LOGIN PASSWORD '{password}'
                    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                CREATE SCHEMA consumer;
                CREATE TABLE consumer.notes (
                    partition text NOT NULL, id integer NOT NULL, body text NOT NULL,
                    PRIMARY KEY (partition, id));
                ALTER TABLE consumer.notes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE consumer.notes FORCE ROW LEVEL SECURITY;
                CREATE POLICY partition_isolation ON consumer.notes FOR ALL TO PUBLIC
                    USING (partition = NULLIF(current_setting('consumer.partition_key', true), ''))
                    WITH CHECK (partition = NULLIF(current_setting('consumer.partition_key', true), ''));
                GRANT USAGE ON SCHEMA consumer TO "{role}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON consumer.notes TO "{role}";
                """,
                connection
            );
            await setup.ExecuteNonQueryAsync(token);
            await using var seed = new NpgsqlCommand(
                """
                INSERT INTO consumer.notes (partition, id, body) VALUES (@alpha, 1, 'Alpha'), (@beta, 1, 'Beta')
                """,
                connection
            );
            seed.Parameters.AddWithValue("alpha", Alpha);
            seed.Parameters.AddWithValue("beta", Beta);
            await seed.ExecuteNonQueryAsync(token);
            return proof;
        }
        catch
        {
            await proof.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        var settings = new NpgsqlConnectionStringBuilder(admin);
        string database = settings.Database!;
        settings.Database = "postgres";
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        using var identifiers = new NpgsqlCommandBuilder();
        await using (
            var drop = new NpgsqlCommand(
                $"DROP DATABASE {identifiers.QuoteIdentifier(database)} WITH (FORCE)",
                connection
            )
        )
            await drop.ExecuteNonQueryAsync(CancellationToken.None);

        await using var dropRole = new NpgsqlCommand(
            $"DROP ROLE IF EXISTS {identifiers.QuoteIdentifier(role)}",
            connection
        );
        await dropRole.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
