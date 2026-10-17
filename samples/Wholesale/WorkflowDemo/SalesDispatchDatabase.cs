using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo;

// Consumer-owned grants for one existing table, not a general role/provisioning framework.
internal static class SalesDispatchDatabase
{
    private static readonly string[] DeliveryColumns =
    [
        "lease_token",
        "lease_until",
        "attempts",
        "available_at",
        "dispatched_at",
    ];

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
                SELECT current_user <> @runtime AND c.relowner = current_user::regrole
                    AND c.relkind = 'r'
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'sales' AND c.relname = 'outbox_messages'
                """,
                connection,
                transaction
            )
        )
        {
            check.Parameters.AddWithValue("runtime", runtime.Username);
            if (await check.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException(
                    "Provision Sales dispatch using the existing outbox owner after explicit module setup."
                );
        }

        // PostgreSQL quotes the identifier and password literal. Existing roles are never altered.
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

        using var identifiers = new NpgsqlCommandBuilder();
        string role = identifiers.QuoteIdentifier(runtime.Username);
        string columns = string.Join(", ", DeliveryColumns.Select(identifiers.QuoteIdentifier));
        await using (
            var grant = new NpgsqlCommand(
                $"""
                GRANT USAGE ON SCHEMA sales TO {role};
                GRANT SELECT ON sales.outbox_messages TO {role};
                GRANT UPDATE ({columns}) ON sales.outbox_messages TO {role};
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
        await using var connection = new NpgsqlConnection(runtimeConnection);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT session_user = current_user AND current_user = @runtime AND r.rolcanlogin
                AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb
                AND NOT r.rolcreaterole AND NOT r.rolreplication
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT has_database_privilege(r.oid, current_database(), 'CREATE')
                AND NOT EXISTS (SELECT 1 FROM pg_database d
                    WHERE d.datname = current_database() AND pg_has_role(r.oid, d.datdba, 'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_namespace n
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                        AND (has_schema_privilege(r.oid, n.oid, 'CREATE')
                            OR pg_has_role(r.oid, n.nspowner, 'MEMBER')))
                AND EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'sales' AND c.relname = 'outbox_messages'
                        AND c.relkind = 'r'
                        AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                        AND has_schema_privilege(r.oid, n.oid, 'USAGE')
                        AND has_table_privilege(r.oid, c.oid, 'SELECT')
                        AND NOT has_table_privilege(r.oid, c.oid,
                            'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        AND NOT EXISTS (SELECT 1 FROM unnest(@columns) required(name)
                            WHERE NOT EXISTS (SELECT 1 FROM pg_attribute a
                                WHERE a.attrelid = c.oid AND a.attnum > 0
                                    AND NOT a.attisdropped AND a.attname = required.name))
                        AND NOT EXISTS (SELECT 1 FROM pg_attribute a
                            WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                                AND (has_column_privilege(r.oid, c.oid, a.attnum, 'UPDATE')
                                        <> (a.attname = ANY(@columns))
                                    OR has_column_privilege(r.oid, c.oid, a.attnum,
                                        'INSERT,REFERENCES,SELECT WITH GRANT OPTION,UPDATE WITH GRANT OPTION')))
                )
                AND NOT EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                        AND NOT (n.nspname = 'sales' AND c.relname = 'outbox_messages')
                        AND ((c.relkind IN ('r', 'p', 'v', 'm', 'f')
                            AND (pg_has_role(r.oid, c.relowner, 'MEMBER')
                                OR has_table_privilege(r.oid, c.oid,
                                'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                                OR has_any_column_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
                            OR (c.relkind = 'S' AND (pg_has_role(r.oid, c.relowner, 'MEMBER')
                                OR has_sequence_privilege(r.oid, c.oid, 'USAGE,SELECT,UPDATE'))))
                )
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            connection
        );
        command.Parameters.AddWithValue("runtime", runtime.Username ?? "");
        command.Parameters.AddWithValue("columns", DeliveryColumns);
        if (
            await command.ExecuteScalarAsync(cancellationToken) is not true
            || !await SalesOutboxPolicies.VerifyAsync(
                connection,
                requireTenantWrites: false,
                cancellationToken
            )
        )
            throw new InvalidOperationException(
                "The Sales dispatch role or outbox grants are unsafe."
            );
    }
}
