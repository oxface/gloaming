using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Persistence.Postgres.Tests;

internal sealed class ProofDatabase(string admin, string role, string runtime) : IAsyncDisposable
{
    internal const string Setting = "consumer.partition_key";
    internal const string Alpha = "tenant:α/'\n$partition";
    internal const string Beta = "tenant:beta";
    internal string Runtime { get; } = runtime;
    internal string Admin { get; } = admin;
    internal Guid AlphaId { get; } = Guid.NewGuid();
    internal Guid BetaId { get; } = Guid.NewGuid();

    internal static async Task<ProofDatabase> CreateAsync(
        PostgreSqlFixture postgres,
        CancellationToken token
    )
    {
        string admin = await postgres.CreateDatabaseAsync(token);
        string role = "rls_runtime_" + Guid.NewGuid().ToString("N");
        string password = Guid.NewGuid().ToString("N");
        var runtime = new NpgsqlConnectionStringBuilder(admin)
        {
            Username = role,
            Password = password,
            Pooling = true,
            MaxPoolSize = 1,
            Timeout = 10,
            MaxAutoPrepare = 8,
            AutoPrepareMinUsages = 1,
        };
        var proof = new ProofDatabase(admin, role, runtime.ConnectionString);
        try
        {
            using var tenancy = new TenantContextAccessor();
            tenancy.Initialize(TenantContext.ForTenant(new(Alpha)));
            await using var database = new ConsumerDatabase(
                new DbContextOptionsBuilder<ConsumerDatabase>().UseNpgsql(admin).Options,
                tenancy
            );
            await database.Database.EnsureCreatedAsync(token);
            database.Notes.AddRange(
                new Note
                {
                    Id = proof.AlphaId,
                    Partition = Alpha,
                    Body = "Alpha",
                },
                new Note
                {
                    Id = proof.BetaId,
                    Partition = Beta,
                    Body = "Beta",
                }
            );
            await database.SaveChangesAsync(token); // Provisioning lane, no runtime interceptors.
            await proof.PoliciesAsync(true, token);
            await proof.AdminSqlAsync(
                $"""
                CREATE ROLE "{role}" LOGIN PASSWORD '{password}'
                    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                GRANT USAGE ON SCHEMA workspace TO "{role}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON workspace."Owned Notes" TO "{role}";
                """,
                token
            );
            return proof;
        }
        catch
        {
            await proof.DisposeAsync();
            throw;
        }
    }

    internal async Task PoliciesAsync(bool enable, CancellationToken token)
    {
        // These are explicit, reviewable migration operations, not request-time DDL or an
        // automatic model-diff generator. Quoted mapped names deliberately exercise SQL generation.
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        if (enable)
            migration.EnableTenantRowSecurity(
                "Owned Notes",
                "Partition\"Key",
                Setting,
                "workspace"
            );
        else
            migration.DisableTenantRowSecurity("Owned Notes", "workspace");

        foreach (var operation in migration.Operations.Cast<SqlOperation>())
            await AdminSqlAsync(operation.Sql, token);
    }

    internal async Task AdminSqlAsync(string sql, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(Admin);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal Task AuthorizingRoleDefaultAsync(CancellationToken token) =>
        AdminSqlAsync($"ALTER ROLE \"{role}\" SET {Setting} = 'unsafe-default'", token);

    internal void ClearPool()
    {
        using var connection = new NpgsqlConnection(Runtime);
        NpgsqlConnection.ClearPool(connection);
    }

    public async ValueTask DisposeAsync()
    {
        ClearPool();
        var settings = new NpgsqlConnectionStringBuilder(Admin);
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
