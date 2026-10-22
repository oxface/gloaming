using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using ModulithFoundry.Samples.Messaging.Rendering;
using Npgsql;

namespace ModulithFoundry.Samples.Messaging.Tests.Fixtures.Inbox;

// Finite consumer maintenance. Provisioning, scheduling and retention horizons stay with the owner.
public static class RenderInboxRetention
{
    private static readonly string[] Columns =
    [
        "subscription_key",
        "producer_key",
        "message_id",
        "receipt_id",
        "processed_at",
        "quarantined_at",
    ];

    public static async Task RunAsync(
        string connection,
        DateTimeOffset processedBefore,
        int batchSize,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await VerifyAsync(connection, cancellationToken);
        await using var database = RenderDbContext.Create(connection);
        int deleted = await database.DeleteProcessedPostgresInboxBatchAsync(
            RenderExportHandler.Subscription,
            "exports",
            processedBefore,
            batchSize,
            cancellationToken
        );
        await output.WriteLineAsync(
            JsonSerializer.Serialize(
                new
                {
                    SubscriptionKey = RenderExportHandler.Subscription,
                    ProducerKey = "exports",
                    ProcessedBefore = processedBefore.ToUniversalTime(),
                    BatchSize = batchSize,
                    Deleted = deleted,
                }
            )
        );
    }

    private static async Task VerifyAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Username);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT session_user = current_user AND current_user = @runtime AND r.rolcanlogin
                AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb
                AND NOT r.rolcreaterole AND NOT r.rolreplication
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT has_database_privilege(r.oid, current_database(), 'CREATE')
                AND NOT EXISTS (SELECT 1 FROM pg_database d WHERE d.datname = current_database()
                    AND pg_has_role(r.oid, d.datdba, 'MEMBER'))
                AND NOT EXISTS (SELECT 1 FROM pg_namespace n
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                        AND (has_schema_privilege(r.oid, n.oid, 'CREATE') OR pg_has_role(r.oid, n.nspowner, 'MEMBER')))
                AND EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'rendering' AND c.relname = 'incoming_work' AND c.relkind = 'r'
                        AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                        AND has_schema_privilege(r.oid, n.oid, 'USAGE')
                        AND has_table_privilege(r.oid, c.oid, 'DELETE')
                        AND NOT has_table_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN,DELETE WITH GRANT OPTION')
                        AND NOT EXISTS (SELECT 1 FROM unnest(@columns) required(name)
                            WHERE NOT EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid
                                AND a.attnum > 0 AND NOT a.attisdropped AND a.attname = required.name))
                        AND NOT EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid
                            AND a.attnum > 0 AND NOT a.attisdropped
                            AND (has_column_privilege(r.oid, c.oid, a.attnum, 'SELECT') <> (a.attname = ANY(@columns))
                                OR has_column_privilege(r.oid, c.oid, a.attnum, 'INSERT,UPDATE,REFERENCES,SELECT WITH GRANT OPTION'))))
                AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
                        AND NOT (n.nspname = 'rendering' AND c.relname = 'incoming_work')
                        AND ((c.relkind IN ('r', 'p', 'v', 'm', 'f')
                            AND (pg_has_role(r.oid, c.relowner, 'MEMBER')
                                OR has_table_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                                OR has_any_column_privilege(r.oid, c.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
                            OR (c.relkind = 'S' AND (pg_has_role(r.oid, c.relowner, 'MEMBER')
                                OR has_sequence_privilege(r.oid, c.oid, 'USAGE,SELECT,UPDATE')))))
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            connection
        );
        command.Parameters.AddWithValue("runtime", settings.Username);
        command.Parameters.AddWithValue("columns", Columns);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException(
                "The Rendering maintenance role or effective inbox grants are unsafe."
            );
    }
}
