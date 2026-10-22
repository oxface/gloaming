using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.Api;

// Finite, explicit provisioning on an already migrated database. Never called by HTTP requests.
public static class SalesHttpRowSecurity
{
    private static readonly string[] Tables =
    [
        "customer_profiles",
        "customer_addresses",
        "draft_orders",
        "draft_order_lines",
        "draft_order_grants",
        "audit_entries",
        "outbox_messages",
    ];

    public static async Task InstallAsync(
        string migrationConnection,
        string runtimeConnection,
        CancellationToken cancellationToken
    )
    {
        var runtime = new NpgsqlConnectionStringBuilder(runtimeConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime.Username);
        using var identifiers = new NpgsqlCommandBuilder();
        string role = identifiers.QuoteIdentifier(runtime.Username);
        await using var connection = new NpgsqlConnection(migrationConnection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (
            var check = new NpgsqlCommand(
                """
                SELECT current_user <> r.rolname AND NOT r.rolsuper AND NOT r.rolbypassrls
                    AND NOT r.rolcreaterole AND NOT r.rolcreatedb
                    AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                    AND NOT has_schema_privilege(r.oid, 'sales', 'CREATE')
                    AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'sales' AND c.relname = ANY(@tables)
                            AND c.relowner = current_user::regrole
                            AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')) = cardinality(@tables)
                FROM pg_roles r WHERE r.rolname = @runtime
                """,
                connection,
                transaction
            )
        )
        {
            check.Parameters.AddWithValue("runtime", runtime.Username);
            check.Parameters.AddWithValue("tables", Tables);
            if (await check.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException(
                    "Use the Sales table owner and a separate restricted runtime role."
                );
        }

        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        foreach (string table in Tables)
            migration.EnableTenantRowSecurity(
                table,
                table switch
                {
                    "audit_entries" => "tenant_key",
                    "outbox_messages" => "owner_key",
                    _ => "organization_key",
                },
                "app.tenant_key",
                "sales"
            );

        foreach (var operation in migration.Operations.Cast<SqlOperation>())
        {
            await using var command = new NpgsqlCommand(operation.Sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (
            var schema = new NpgsqlCommand(
                $"GRANT USAGE ON SCHEMA sales TO {role}",
                connection,
                transaction
            )
        )
            await schema.ExecuteNonQueryAsync(cancellationToken);

        foreach (string table in Tables)
        {
            string privileges = table switch
            {
                "draft_order_grants" => "SELECT",
                "audit_entries" or "outbox_messages" => "SELECT, INSERT",
                _ => "SELECT, INSERT, UPDATE, DELETE",
            };
            await using var grant = new NpgsqlCommand(
                $"GRANT {privileges} ON sales.{identifiers.QuoteIdentifier(table)} TO {role}",
                connection,
                transaction
            );
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Startup checks the actual Sales credential and finite catalog, without tenant business SQL.
    // Production policy bodies, functions/views and all other host credentials still need review.
    public static async Task VerifyRuntimeAsync(
        string runtimeConnection,
        string accessConnection,
        CancellationToken cancellationToken
    )
    {
        var settings = new NpgsqlConnectionStringBuilder(runtimeConnection);
        if (settings.NoResetOnClose || settings.Multiplexing)
            throw new InvalidOperationException(
                "Use reset-enabled, non-multiplexed Sales connections."
            );

        await using var connection = new NpgsqlConnection(runtimeConnection);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT session_user = current_user AND current_user = @runtime
                AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole AND NOT r.rolcreatedb
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT has_schema_privilege(r.oid, 'sales', 'CREATE')
                AND NULLIF(current_setting('app.tenant_key', true), '') IS NULL
                AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'sales' AND c.relname = ANY(@tables)
                        AND c.relrowsecurity AND c.relforcerowsecurity
                        AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                        AND has_table_privilege(r.oid, c.oid, 'SELECT')
                        AND NOT has_table_privilege(r.oid, c.oid, 'TRUNCATE')
                        AND (c.relname = 'draft_order_grants'
                            OR (has_table_privilege(r.oid, c.oid, 'INSERT')
                                AND (c.relname IN ('audit_entries', 'outbox_messages')
                                    OR (has_table_privilege(r.oid, c.oid, 'UPDATE')
                                        AND has_table_privilege(r.oid, c.oid, 'DELETE')))))
                        AND (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid) = 1
                        AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid
                            AND p.polname = 'gloaming_tenant' AND p.polcmd = '*'
                            AND p.polpermissive AND p.polroles = ARRAY[0::oid]
                            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL)) = cardinality(@tables)
                AND NOT has_table_privilege(r.oid, 'sales.draft_order_grants', 'INSERT,UPDATE,DELETE')
                AND NOT has_table_privilege(r.oid, 'sales.audit_entries', 'UPDATE,DELETE')
                AND NOT has_table_privilege(r.oid, 'sales.outbox_messages', 'UPDATE,DELETE')
                AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'sales' AND c.relkind IN ('r', 'p') AND c.relname <> ALL(@tables)
                        AND has_table_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE'))
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            connection
        );
        command.Parameters.AddWithValue("runtime", settings.Username ?? "");
        command.Parameters.AddWithValue("tables", Tables);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException(
                "The Sales HTTP runtime role, policies or tenant baseline are unsafe."
            );

        // The same HTTP process must not retain a second credential that bypasses Sales isolation.
        await using var access = new NpgsqlConnection(accessConnection);
        await access.OpenAsync(cancellationToken);
        await using var accessCheck = new NpgsqlCommand(
            """
            SELECT session_user = current_user AND NOT r.rolsuper AND NOT r.rolbypassrls
                AND NOT r.rolcreaterole AND NOT r.rolcreatedb
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT has_schema_privilege(r.oid, 'sales', 'CREATE')
                AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'sales' AND c.relkind IN ('r', 'p')
                        AND has_table_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE'))
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            access
        );
        if (await accessCheck.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException(
                "The HTTP Access/Inventory credential must have no Sales table authority."
            );
    }
}
