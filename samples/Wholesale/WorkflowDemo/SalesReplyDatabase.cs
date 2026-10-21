using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo;

// Finite sample provisioning against existing module storage. No worker-startup DDL.
internal static class SalesReplyDatabase
{
    private static readonly Dictionary<string, string> ProtectedTables = new(StringComparer.Ordinal)
    {
        ["workflow_instances"] = "TenantKey",
        ["inbox_messages"] = "tenant_key",
    };
    private static readonly string[] InboxUpdates =
    [
        "processed_at",
        "available_at",
        "revision",
        "failure_count",
        "failures_since_redrive",
        "last_failed_at",
        "last_failure_code",
        "quarantined_at",
    ];
    private static readonly string[] OutboxReads = ["queued_at", "available_at"];

    internal static async Task ProvisionAsync(
        string setupConnection,
        string runtimeConnection,
        CancellationToken cancellationToken
    )
    {
        var runtime = new NpgsqlConnectionStringBuilder(runtimeConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime.Username);
        ArgumentException.ThrowIfNullOrEmpty(runtime.Password);

        await using var connection = new NpgsqlConnection(setupConnection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (
            var check = new NpgsqlCommand(
                """
                SELECT current_user <> @runtime AND NOT EXISTS (
                    SELECT 1 FROM unnest(@tables) required(name)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'sales' AND c.relname = required.name
                            AND c.relkind = 'r' AND c.relowner = current_user::regrole AND NOT c.relrowsecurity))
                """,
                connection,
                transaction
            )
        )
        {
            check.Parameters.AddWithValue("runtime", runtime.Username);
            check.Parameters.AddWithValue(
                "tables",
                ProtectedTables.Keys.Append("outbox_messages").ToArray()
            );
            if (await check.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException(
                    "Provision Sales replies using the existing table owner after explicit module setup."
                );
        }

        await using (
            var format = new NpgsqlCommand(
                "SELECT format('CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT', @role, @password)",
                connection,
                transaction
            )
        )
        {
            format.Parameters.AddWithValue("role", runtime.Username);
            format.Parameters.AddWithValue("password", runtime.Password);
            string sql = (string)(await format.ExecuteScalarAsync(cancellationToken))!;
            await using var create = new NpgsqlCommand(sql, connection, transaction);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        foreach (var (table, column) in ProtectedTables)
            migration.EnableTenantRowSecurity(table, column, "app.tenant_key", "sales");

        foreach (var operation in migration.Operations.Cast<SqlOperation>())
        {
            await using var policy = new NpgsqlCommand(operation.Sql, connection, transaction);
            await policy.ExecuteNonQueryAsync(cancellationToken);
        }

        await SalesOutboxPolicies.InstallAsync(connection, transaction, cancellationToken);

        using var identifiers = new NpgsqlCommandBuilder();
        string role = identifiers.QuoteIdentifier(runtime.Username);
        string updates = string.Join(", ", InboxUpdates.Select(identifiers.QuoteIdentifier));
        string reads = string.Join(", ", OutboxReads.Select(identifiers.QuoteIdentifier));
        await using (
            var grant = new NpgsqlCommand(
                $"""
                GRANT USAGE ON SCHEMA sales TO {role};
                GRANT SELECT, UPDATE ON sales.workflow_instances TO {role};
                GRANT SELECT ON sales.inbox_messages TO {role};
                GRANT UPDATE ({updates}) ON sales.inbox_messages TO {role};
                GRANT INSERT ON sales.outbox_messages TO {role};
                GRANT SELECT ({reads}) ON sales.outbox_messages TO {role};
                """,
                connection,
                transaction
            )
        )
            await grant.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        await VerifyAsync(runtimeConnection, cancellationToken);
    }

    internal static async Task VerifyAsync(
        string runtimeConnection,
        CancellationToken cancellationToken
    )
    {
        var runtime = new NpgsqlConnectionStringBuilder(runtimeConnection);
        if (runtime.NoResetOnClose || runtime.Multiplexing)
            throw new InvalidOperationException(
                "Use reset-enabled, non-multiplexed Sales reply connections."
            );

        await using var connection = new NpgsqlConnection(runtimeConnection);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT session_user = current_user AND current_user = @runtime AND r.rolcanlogin
                AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb
                AND NOT r.rolcreaterole AND NOT r.rolreplication
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NULLIF(current_setting('app.tenant_key', true), '') IS NULL
                AND NOT has_database_privilege(r.oid, current_database(), 'CREATE')
                AND NOT EXISTS (SELECT 1 FROM pg_database d
                    WHERE d.datname = current_database() AND pg_has_role(r.oid, d.datdba, 'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_namespace n
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                        AND (has_schema_privilege(r.oid, n.oid, 'CREATE')
                            OR pg_has_role(r.oid, n.nspowner, 'MEMBER')))
                AND NOT EXISTS (
                    SELECT 1 FROM unnest(@protected) required(name)
                    WHERE NOT EXISTS (
                        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'sales' AND c.relname = required.name AND c.relkind = 'r'
                            AND c.relrowsecurity AND c.relforcerowsecurity
                            AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                            AND has_schema_privilege(r.oid, n.oid, 'USAGE')
                            AND has_table_privilege(r.oid, c.oid, 'SELECT')
                            AND NOT has_table_privilege(r.oid, c.oid, 'INSERT,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                            AND NOT has_any_column_privilege(r.oid, c.oid, 'INSERT,REFERENCES,SELECT WITH GRANT OPTION,UPDATE WITH GRANT OPTION')
                            AND ((c.relname = 'workflow_instances' AND has_table_privilege(r.oid, c.oid, 'UPDATE'))
                                OR (c.relname = 'inbox_messages' AND NOT has_table_privilege(r.oid, c.oid, 'UPDATE')
                                    AND NOT EXISTS (SELECT 1 FROM pg_attribute a
                                        WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                                            AND has_column_privilege(r.oid, c.oid, a.attnum, 'UPDATE') <> (a.attname = ANY(@inboxUpdates)))))
                            AND (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid) = 1
                            AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid
                                AND p.polname = 'gloaming_tenant' AND p.polcmd = '*' AND p.polpermissive
                                AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL)
                    )
                )
                AND EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'sales' AND c.relname = 'outbox_messages' AND c.relkind = 'r'
                        AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                        AND has_table_privilege(r.oid, c.oid, 'INSERT')
                        AND NOT has_table_privilege(r.oid, c.oid, 'SELECT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        AND NOT has_any_column_privilege(r.oid, c.oid, 'UPDATE,REFERENCES,INSERT WITH GRANT OPTION,SELECT WITH GRANT OPTION')
                        AND NOT EXISTS (SELECT 1 FROM pg_attribute a
                            WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                                AND has_column_privilege(r.oid, c.oid, a.attnum, 'SELECT') <> (a.attname = ANY(@outboxReads)))
                )
                AND NOT EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                        AND NOT (n.nspname = 'sales' AND c.relname = ANY(@allTables))
                        AND ((c.relkind IN ('r', 'p', 'v', 'm', 'f')
                            AND (pg_has_role(r.oid, c.relowner, 'MEMBER')
                                OR has_table_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                                OR has_any_column_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
                            OR (c.relkind = 'S' AND (pg_has_role(r.oid, c.relowner, 'MEMBER')
                                OR has_sequence_privilege(r.oid, c.oid, 'USAGE,SELECT,UPDATE'))))
                )
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            connection
        );
        command.Parameters.AddWithValue("runtime", runtime.Username ?? "");
        command.Parameters.AddWithValue("protected", ProtectedTables.Keys.ToArray());
        command.Parameters.AddWithValue(
            "allTables",
            ProtectedTables.Keys.Append("outbox_messages").ToArray()
        );
        command.Parameters.AddWithValue("inboxUpdates", InboxUpdates);
        command.Parameters.AddWithValue("outboxReads", OutboxReads);
        if (
            await command.ExecuteScalarAsync(cancellationToken) is not true
            || !await SalesOutboxPolicies.VerifyAsync(
                connection,
                requireTenantWrites: true,
                cancellationToken
            )
        )
            throw new InvalidOperationException(
                "The Sales reply role, policy inventory or grants are unsafe."
            );
    }
}
