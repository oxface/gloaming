using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using ModulithFoundry.Samples.Wholesale.Api;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    private async Task<RowSecurityHost> StartRowSecurityAsync(
        Action<WebApplication>? endpoints = null,
        bool commandSetup = false
    )
    {
        string admin = await postgres.CreateDatabaseAsync(Token);
        string suffix = Guid.NewGuid().ToString("N");
        string salesRole = "http_sales_" + suffix;
        string accessRole = "http_access_" + suffix;
        string password = Guid.NewGuid().ToString("N");
        var proof = new RowSecurityHost(admin, salesRole, accessRole);
        try
        {
            // Migrations/seeding run only in this finite privileged fixture; neither runtime gets it.
            await using (var setup = await StartAsync(connectionString: admin)) { }

            await proof.SqlAsync(
                $"""
                CREATE ROLE "{salesRole}" LOGIN PASSWORD '{password}'
                    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                CREATE ROLE "{accessRole}" LOGIN PASSWORD '{password}'
                    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                GRANT USAGE ON SCHEMA access, inventory TO "{accessRole}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA access TO "{accessRole}";
                GRANT SELECT ON inventory.stock_availability TO "{accessRole}";
                -- Seed the earlier publication grant; the finite installer must remove it.
                GRANT SELECT, INSERT ON sales.outbox_messages TO "{salesRole}";
                """
            );
            proof.SalesConnection = new NpgsqlConnectionStringBuilder(admin)
            {
                Username = salesRole,
                Password = password,
                Pooling = true,
                MaxPoolSize = 1,
            }.ConnectionString;
            string access = new NpgsqlConnectionStringBuilder(admin)
            {
                Username = accessRole,
                Password = password,
            }.ConnectionString;
            if (commandSetup)
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add(typeof(DemoComposition).Assembly.Location);
                start.ArgumentList.Add("--initialize-sales-rls");
                start.Environment["ConnectionStrings__Access"] = access;
                start.Environment["ConnectionStrings__Sales"] = proof.SalesConnection;
                start.Environment["ConnectionStrings__SalesMigrations"] = admin;
                using var process =
                    Process.Start(start)
                    ?? throw new InvalidOperationException("Could not start Sales setup.");
                Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(Token);
                try
                {
                    await process
                        .WaitForExitAsync(Token)
                        .WaitAsync(TimeSpan.FromSeconds(30), Token);
                    Assert.Equal(0, process.ExitCode);
                    Assert.Empty(await errors);
                    Assert.Contains(
                        "HTTP host was not started",
                        await output,
                        StringComparison.Ordinal
                    );
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
            }
            else
                await SalesHttpRowSecurity.InstallAsync(admin, proof.SalesConnection, Token);

            proof.App = await StartAsync(
                connectionString: access,
                initialize: false,
                afterContext: endpoints,
                configureHost: builder =>
                    builder.Configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Sales"] = proof.SalesConnection,
                        }
                    )
            );
            return proof;
        }
        catch
        {
            await proof.DisposeAsync();
            throw;
        }
    }

    private sealed class RowSecurityHost(string admin, string salesRole, string accessRole)
        : IAsyncDisposable
    {
        internal WebApplication App { get; set; } = null!;
        internal string SalesConnection { get; set; } = "";

        internal string SalesRole { get; } = salesRole;

        internal Task VerifyPrivilegedAccessAsync() =>
            SalesHttpRowSecurity.VerifyRuntimeAsync(SalesConnection, admin, Token);

        internal async Task SqlAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync(Token);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(Token);
        }

        internal async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync(Token);
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync(Token))!;
        }

        public async ValueTask DisposeAsync()
        {
            if (App is not null)
                await App.DisposeAsync();

            if (SalesConnection.Length > 0)
            {
                using var pool = new NpgsqlConnection(SalesConnection);
                NpgsqlConnection.ClearPool(pool);
            }

            var settings = new NpgsqlConnectionStringBuilder(admin);
            string database = settings.Database!;
            settings.Database = "postgres";
            using var identifiers = new NpgsqlCommandBuilder();
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var drop = new NpgsqlCommand(
                $"""
                DROP DATABASE {identifiers.QuoteIdentifier(database)} WITH (FORCE);
                DROP ROLE IF EXISTS {identifiers.QuoteIdentifier(SalesRole)};
                DROP ROLE IF EXISTS {identifiers.QuoteIdentifier(accessRole)};
                """,
                connection
            );
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
