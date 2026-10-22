using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.Worker.Broker;

// Queue authority differs by command: enqueue belongs to an operation tenant; delivery scans all tenants.
internal static class SalesOutboxPolicies
{
    internal static async Task InstallAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            """
            ALTER TABLE sales.outbox_messages ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sales.outbox_messages FORCE ROW LEVEL SECURITY;
            CREATE POLICY gloaming_outbox_enqueue ON sales.outbox_messages FOR INSERT TO PUBLIC
                WITH CHECK (owner_key COLLATE "C" = NULLIF(current_setting('app.tenant_key', true), ''));
            CREATE POLICY gloaming_outbox_read ON sales.outbox_messages FOR SELECT TO PUBLIC USING (true);
            CREATE POLICY gloaming_outbox_delivery ON sales.outbox_messages FOR UPDATE TO PUBLIC
                USING (true) WITH CHECK (true);
            """,
            connection,
            transaction
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<bool> VerifyAsync(
        NpgsqlConnection connection,
        bool requireTenantWrites,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            """
            WITH expected(name, operation) AS (VALUES
                ('gloaming_outbox_enqueue', 'a'), ('gloaming_outbox_read', 'r'), ('gloaming_outbox_delivery', 'w'))
            SELECT (NOT @required AND NOT c.relrowsecurity) OR (
                c.relrowsecurity AND c.relforcerowsecurity
                AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                    SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = e.name
                        AND p.polcmd::text = e.operation AND p.polpermissive AND p.polroles = ARRAY[0::oid]
                        AND CASE e.operation
                            WHEN 'a' THEN p.polqual IS NULL AND p.polwithcheck IS NOT NULL
                            WHEN 'r' THEN pg_get_expr(p.polqual, c.oid) = 'true' AND p.polwithcheck IS NULL
                            WHEN 'w' THEN pg_get_expr(p.polqual, c.oid) = 'true' AND pg_get_expr(p.polwithcheck, c.oid) = 'true'
                            ELSE false END))
                AND NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid
                    AND NOT EXISTS (SELECT 1 FROM expected e WHERE e.name = p.polname))
            )
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'sales' AND c.relname = 'outbox_messages' AND c.relkind = 'r'
            """,
            connection
        );
        command.Parameters.AddWithValue("required", requireTenantWrites);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
