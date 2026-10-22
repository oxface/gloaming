using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.Api;

// Readiness is sample schema/connectivity policy. It never queries tenant business rows or repairs schema.
internal sealed class WholesaleDatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = new NpgsqlConnection(
            configuration.GetConnectionString("Access")
        );
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                WITH required_tables(schema_name, table_name) AS (
                    VALUES ('access', 'users'), ('access', 'external_identities'),
                           ('access', 'organizations'), ('access', 'memberships'),
                           ('inventory', 'stock_availability'),
                           ('sales', 'customer_profiles'), ('sales', 'customer_addresses')
                )
                SELECT NOT EXISTS (
                    SELECT 1 FROM required_tables required
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = required.schema_name
                          AND c.relname = required.table_name
                          AND c.relkind IN ('r', 'p')
                    )
                )
                """,
                connection
            );
            return Equals(true, await command.ExecuteScalarAsync(cancellationToken))
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Required Wholesale tables are missing.");
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("The Wholesale database is unavailable.", exception);
        }
    }
}
