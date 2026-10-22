using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs.TenantIsolation;

// Explicit consumer setup on an already migrated disposable database, never HTTP startup.
internal static class SalesRowSecurity
{
    internal static IReadOnlyList<string> Tables { get; } =
    [
        "customer_profiles",
        "customer_addresses",
        "draft_orders",
        "draft_order_lines",
        "draft_order_grants",
    ];

    internal static async Task ApplyAsync(
        string migrationConnection,
        string runtimeRole,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRole);

        await using var connection = new NpgsqlConnection(migrationConnection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (
            var check = new NpgsqlCommand(
                """
                SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole AND NOT r.rolcreatedb
                    AND NOT EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = r.oid)
                    AND NOT pg_has_role(r.oid, owner.oid, 'MEMBER')
                    AND NOT owner.rolsuper AND NOT owner.rolbypassrls
                FROM pg_roles r CROSS JOIN pg_roles owner
                WHERE r.rolname = @runtime AND owner.rolname = current_user
                """,
                connection,
                transaction
            )
        )
        {
            check.Parameters.AddWithValue("runtime", runtimeRole);
            if (await check.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException(
                    "Use separate unprivileged runtime and migration-owner roles."
                );
        }

        using var identifiers = new NpgsqlCommandBuilder();
        string quotedRole = identifiers.QuoteIdentifier(runtimeRole);
        await using (
            var grantSchema = new NpgsqlCommand(
                $"GRANT USAGE ON SCHEMA sales TO {quotedRole}",
                connection,
                transaction
            )
        )
            await grantSchema.ExecuteNonQueryAsync(cancellationToken);

        foreach (string table in Tables)
        {
            // Table names come only from the finite consumer inventory above.
            string name = "sales." + identifiers.QuoteIdentifier(table);
            await using var command = new NpgsqlCommand(
                $"""
                ALTER TABLE {name} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {name} FORCE ROW LEVEL SECURITY;
                CREATE POLICY organization_isolation ON {name} FOR ALL TO PUBLIC
                    USING (organization_key = NULLIF(current_setting('app.tenant_key', true), ''))
                    WITH CHECK (organization_key = NULLIF(current_setting('app.tenant_key', true), ''));
                GRANT SELECT ON {name} TO {quotedRole};
                """,
                connection,
                transaction
            );
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (table != "draft_order_grants")
            {
                await using var grant = new NpgsqlCommand(
                    $"GRANT INSERT, UPDATE, DELETE ON {name} TO {quotedRole}",
                    connection,
                    transaction
                );
                await grant.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task VerifyRuntimeAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken
    )
    {
        var settings = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT current_user = @runtime AND session_user = current_user
                AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole AND NOT r.rolcreatedb
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = r.oid)
                AND NOT has_schema_privilege(current_user, 'sales', 'CREATE')
                AND NULLIF(current_setting('app.tenant_key', true), '') IS NULL
                AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'sales' AND c.relname = ANY(@tables)
                        AND c.relrowsecurity AND c.relforcerowsecurity
                        AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')) = 5
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            connection
        );
        command.Parameters.AddWithValue("runtime", settings.Username!);
        command.Parameters.AddWithValue("tables", Tables.ToArray());
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException(
                "The actual runtime role, RLS inventory or tenantless baseline is unsafe for this composition."
            );
    }
}
