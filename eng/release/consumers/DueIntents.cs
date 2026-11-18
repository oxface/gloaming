using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("GLOAMING_PACKAGE_TEST_CONNECTION_STRING is required.");
string name = "gloaming_timers_" + Guid.NewGuid().ToString("N");
string role = "gloaming_timer_worker_" + Guid.NewGuid().ToString("N");
string password = Guid.NewGuid().ToString("N");
var migrationSettings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(migrationSettings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync(token);

migrationSettings.Database = name;
var runtimeSettings = new NpgsqlConnectionStringBuilder(migrationSettings.ConnectionString)
{
    Username = role,
    Password = password,
};
DateTimeOffset due = new DateTimeOffset(2035, 3, 15, 9, 0, 0, TimeSpan.Zero).AddTicks(1_234_500);
var legacyKey = new DueIntentKey(
    "admitted",
    Guid.Parse("11111111-1111-1111-1111-111111111111"),
    Guid.Parse("00000000-0000-0000-0000-000000000001")
);
var globalKey = new DueIntentKey("module-worker", Guid.NewGuid(), Guid.NewGuid());
Exception? failure = null;
try
{
    await using (var setup = Open(migrationSettings.ConnectionString))
    {
        // An owned earlier native layout has real retained data before the forward migration.
        await setup
            .GetService<IMigrator>()
            .MigrateAsync("20261024000100_LegacyOccurrenceJournal", token);
        Check(
            await setup
                .Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM clockwork.due_intents"
                )
                .SingleAsync(token) == 3,
            "The earlier schema must already contain all three retained lifecycle states."
        );
        await setup.Database.MigrateAsync(token);
        Check(
            (await setup.Database.GetAppliedMigrationsAsync(token)).Count() == 2,
            "Both consumer migrations must be applied."
        );
        var rows = await setup
            .Set<DueIntentRecord>()
            .OrderBy(row => row.OccurrenceId)
            .ToArrayAsync(token);
        Check(
            rows.Select(row => row.State)
                .SequenceEqual(
                    new[]
                    {
                        DueIntentState.Pending,
                        DueIntentState.Cancelled,
                        DueIntentState.Released,
                    }
                ),
            "Forward migration must preserve retained lifecycle evidence."
        );
        Guid[] occurrenceIds =
        [
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
        ];
        Guid[] deliveryIds =
        [
            Guid.Parse("00000000-0000-0000-0000-000000000101"),
            Guid.Parse("00000000-0000-0000-0000-000000000102"),
            Guid.Parse("00000000-0000-0000-0000-000000000103"),
        ];
        for (int index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            Check(
                row.ScopeKey == "admitted"
                    && row.ScheduleId == legacyKey.ScheduleId
                    && row.OccurrenceId == occurrenceIds[index]
                    && row.MessageId == deliveryIds[index]
                    && row.TenantKey == "admitted"
                    && row.DueAt.EqualsExact(due)
                    && row.RouteKey == "legacy-route"
                    && row.MessageName == "future.contract"
                    && row.SchemaVersion == 99
                    && JsonElement.DeepEquals(
                        row.Payload,
                        JsonSerializer.SerializeToElement(new { amount = 17, legacy = "v0" })
                    )
                    && row.CorrelationId == "original-conversation"
                    && row.CausationId == "original-cause"
                    && row.TraceParent == "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01"
                    && row.TraceState == "legacy=value",
                "Migration must preserve the original complete wire envelope and microsecond due instant."
            );
        }

        var original = await setup.Set<OutboxMessageRecord>().SingleAsync(token);
        Check(
            original.MessageId == deliveryIds[2]
                && original.TenantKey == "admitted"
                && original.RouteKey == "legacy-route"
                && original.MessageName == "future.contract"
                && original.SchemaVersion == 99
                && JsonElement.DeepEquals(
                    original.Payload,
                    JsonSerializer.SerializeToElement(new { amount = 17, legacy = "v0" })
                )
                && original.CorrelationId == "original-conversation"
                && original.CausationId == "original-cause"
                && original.TraceParent == "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01"
                && original.TraceState == "legacy=value"
                && original.QueuedAt.EqualsExact(
                    new DateTimeOffset(2034, 3, 15, 9, 0, 0, TimeSpan.Zero)
                ),
            "Forward migration must leave the original outbox handoff identity, envelope and insertion time intact."
        );
    }

    await using (var provision = new NpgsqlConnection(migrationSettings.ConnectionString))
    {
        await provision.OpenAsync(token);
        await using var grant = new NpgsqlCommand(
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CONNECT ON DATABASE {name} TO "{role}";
            GRANT USAGE ON SCHEMA clockwork TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON clockwork.due_intents TO "{role}";
            GRANT SELECT, INSERT ON clockwork.outbox TO "{role}";
            """,
            provision
        );
        await grant.ExecuteNonQueryAsync(token);
    }

    await using (var native = new NpgsqlConnection(runtimeSettings.ConnectionString))
    {
        await native.OpenAsync(token);
        await using var audit = new NpgsqlCommand(
            """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT has_schema_privilege(current_user, 'clockwork', 'CREATE')
                AND NOT has_table_privilege(current_user, 'clockwork.due_intents', 'DELETE')
                AND NOT has_table_privilege(current_user, 'clockwork.outbox', 'UPDATE')
                AND NOT pg_has_role(r.oid, (SELECT relowner FROM pg_class WHERE oid = 'clockwork.due_intents'::regclass), 'MEMBER')
            FROM pg_roles r WHERE rolname = current_user
            """,
            native
        );
        Check(
            await audit.ExecuteScalarAsync(token) is true,
            "The module worker must have only its declared runtime grants."
        );
        await using var denied = new NpgsqlCommand("DELETE FROM clockwork.due_intents", native);
        try
        {
            await denied.ExecuteNonQueryAsync(token);
            throw new InvalidOperationException("Runtime retention deletion must be denied.");
        }
        catch (PostgresException error)
            when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var intents = new PostgresDueIntents<TimerDatabase>(database);
        Check(
            (await intents.ReleaseAsync("admitted", due.AddTicks(-10), 10, token)).Count == 0,
            "No occurrence may be released one microsecond early."
        );
        await transaction.CommitAsync(token);
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var intents = new PostgresDueIntents<TimerDatabase>(database);
        var released = await intents.ReleaseAsync("admitted", due, 10, token);
        Check(
            released.SequenceEqual(new[] { Guid.Parse("00000000-0000-0000-0000-000000000101") }),
            "Only the original pending generation is handed off."
        );
        await database.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: token);
        database.ChangeTracker.AcceptAllChanges();
        Check(
            database.SaveChanges() == 0 && await database.SaveChangesAsync(token) == 0,
            "Stored handoff supports both repeated native save forms."
        );
        await transaction.CommitAsync(token);
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        var old = await database
            .Set<OutboxMessageRecord>()
            .SingleAsync(
                row => row.MessageId == Guid.Parse("00000000-0000-0000-0000-000000000101"),
                token
            );
        Check(
            old.TenantKey == "admitted"
                && old.SchemaVersion == 99
                && old.Payload.GetProperty("amount").GetInt32() == 17,
            "Handoff must be opaque to a future consumer schema and preserve tenant metadata."
        );
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var intents = new PostgresDueIntents<TimerDatabase>(database);
        Check(
            await intents.SuppressAsync(legacyKey, token) == DueIntentSuppression.AlreadyReleased,
            "Committed handoff cannot be recalled."
        );
        Check(
            await intents.SuppressAsync(
                new(
                    "admitted",
                    legacyKey.ScheduleId,
                    Guid.Parse("00000000-0000-0000-0000-000000000002")
                ),
                token
            ) == DueIntentSuppression.AlreadyCancelled,
            "Retained cancellation must survive migration."
        );
        Check(
            await intents.SuppressAsync(
                new("admitted", legacyKey.ScheduleId, Guid.NewGuid()),
                token
            ) == DueIntentSuppression.Missing,
            "Missing evidence differs from cancellation."
        );
        await transaction.CommitAsync(token);
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        new PostgresDueIntents<TimerDatabase>(database).Stage(
            globalKey,
            due,
            new(
                Guid.NewGuid(),
                "global-route",
                "global.reminder",
                1,
                JsonSerializer.SerializeToElement(new { value = 31 })
            )
        );
        database.SaveChanges();
        await transaction.RollbackAsync(token);
    }
    await Counts(3, 2);

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        new PostgresDueIntents<TimerDatabase>(database).Stage(
            globalKey,
            due,
            new(
                Guid.NewGuid(),
                "global-route",
                "global.reminder",
                1,
                JsonSerializer.SerializeToElement(new { value = 31 })
            )
        );
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    foreach (bool suppressOccurrence in new[] { true, false })
    {
        await using var database = Open(runtimeSettings.ConnectionString);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await new PostgresDueIntents<TimerDatabase>(database).ReleaseAsync(
            "module-worker",
            due,
            1,
            token
        );
        if (suppressOccurrence)
            database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
                EntityState.Unchanged;
        else
            database.ChangeTracker.Entries<OutboxMessageRecord>().Single().State =
                EntityState.Unchanged;

        bool rejected = false;
        try
        {
            await database.SaveChangesAsync(token);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Check(
            rejected,
            "Neither side of the handoff may be suppressed, including under restricted native reads."
        );
        await transaction.RollbackAsync(token);
    }
    await Counts(4, 2);

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var intents = new PostgresDueIntents<TimerDatabase>(database);
        await transaction.CreateSavepointAsync("before_release", token);
        await intents.ReleaseAsync("module-worker", due, 1, token);
        await database.SaveChangesAsync(token);
        await transaction.RollbackToSavepointAsync("before_release", token);
        bool rejected = false;
        try
        {
            database.SaveChanges();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Check(
            rejected,
            "Unchanged handoff rows undone by a savepoint are not stored participation."
        );
        await transaction.RollbackAsync(token);
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        Check(
            (
                await new PostgresDueIntents<TimerDatabase>(database).ReleaseAsync(
                    "module-worker",
                    due,
                    1,
                    token
                )
            ).Count == 1,
            "Fresh recovery must release exactly the pending occurrence."
        );
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
    await Counts(4, 3);

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        var global = await database
            .Set<OutboxMessageRecord>()
            .SingleAsync(row => row.RouteKey == "global-route", token);
        Check(
            global.TenantKey is null && global.Payload.GetProperty("value").GetInt32() == 31,
            "Technical scope must support an independently selected tenantless envelope."
        );
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        Check(
            (
                await new PostgresDueIntents<TimerDatabase>(database).ReleaseAsync(
                    "module-worker",
                    due,
                    100,
                    token
                )
            ).Count == 0,
            "A fresh release after a committed handoff must not hand off the occurrence again."
        );
        await transaction.CommitAsync(token);
    }

    var cancelledKey = new DueIntentKey("module-worker", globalKey.ScheduleId, Guid.NewGuid());
    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        new PostgresDueIntents<TimerDatabase>(database).Stage(
            cancelledKey,
            due.AddDays(1),
            new(
                Guid.NewGuid(),
                "cancelled-route",
                "obsolete.reminder",
                1,
                JsonSerializer.SerializeToElement(new { value = 32 })
            )
        );
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        Check(
            await new PostgresDueIntents<TimerDatabase>(database).SuppressAsync(cancelledKey, token)
                == DueIntentSuppression.Suppressed,
            "Pending future work must be suppressible before handoff."
        );
        database.SaveChanges();
        await transaction.CommitAsync(token);
    }

    await using (var database = Open(runtimeSettings.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        Check(
            (
                await new PostgresDueIntents<TimerDatabase>(database).ReleaseAsync(
                    "module-worker",
                    due.AddDays(2),
                    100,
                    token
                )
            ).Count == 0,
            "A committed cancellation must remain excluded on a late release."
        );
        await transaction.CommitAsync(token);
    }

    await Counts(5, 3);
    Console.WriteLine(
        "PASS due intents: populated migration, restricted module worker, immutable opaque handoff, native rollback and pair recovery"
    );
}
catch (Exception error)
{
    failure = error;
    throw;
}
finally
{
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try
    {
        await using (var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin))
            await drop.ExecuteNonQueryAsync(cleanup.Token);

        await using var dropRole = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{role}\"", admin);
        await dropRole.ExecuteNonQueryAsync(cleanup.Token);
    }
    catch (Exception error) when (failure is not null)
    {
        Console.Error.WriteLine($"Due-intent cleanup failed ({error.GetType().Name}).");
    }
}

TimerDatabase Open(string connection) =>
    new(new DbContextOptionsBuilder<TimerDatabase>().UseNpgsql(connection).Options);
async Task Counts(int occurrences, int outgoing)
{
    await using var database = Open(runtimeSettings.ConnectionString);
    Check(
        await database.Set<DueIntentRecord>().CountAsync(token) == occurrences,
        "Retained occurrence count."
    );
    Check(
        await database.Set<OutboxMessageRecord>().CountAsync(token) == outgoing,
        "Atomic outbox count."
    );
}
static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class TimerDatabase(DbContextOptions<TimerDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigurePostgresOutbox("clockwork", "outbox");
        model.ConfigurePostgresDueIntents("clockwork");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateDueIntentChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        await this.ValidateDueIntentChangesAsync(cancellationToken);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

[DbContext(typeof(TimerDatabase))]
[Migration("20261024000100_LegacyOccurrenceJournal")]
sealed class LegacyOccurrenceJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE SCHEMA clockwork;
            CREATE TABLE clockwork.due_intents (
                scope_key text COLLATE "C" NOT NULL, schedule_id uuid NOT NULL, occurrence_id uuid NOT NULL,
                due_at timestamptz NOT NULL, state integer NOT NULL, "MessageId" uuid NOT NULL,
                "Route" text NOT NULL, "Name" text NOT NULL, "SchemaVersion" integer NOT NULL, "Payload" text NOT NULL,
                "CorrelationId" text, "CausationId" text, "TraceParent" text, "TraceState" text,
                PRIMARY KEY (scope_key, schedule_id, occurrence_id));
            INSERT INTO clockwork.due_intents
                SELECT 'admitted', '11111111-1111-1111-1111-111111111111'::uuid,
                    ('00000000-0000-0000-0000-' || lpad(value::text, 12, '0'))::uuid,
                    '2035-03-15 09:00:00.123450+00'::timestamptz, value,
                    ('00000000-0000-0000-0000-' || lpad((value+100)::text, 12, '0'))::uuid,
                    'legacy-route', 'future.contract', 99, '{"amount":17,"legacy":"v0"}',
                    'original-conversation', 'original-cause',
                    '00-0123456789abcdef0123456789abcdef-0123456789abcdef-01', 'legacy=value'
                FROM generate_series(1,3) value;
            CREATE TABLE clockwork.outbox (
                message_id uuid PRIMARY KEY, destination text NOT NULL, message_name text NOT NULL,
                schema_version integer NOT NULL, payload jsonb NOT NULL, owner_key text,
                correlation_id text, causation_id text, trace_parent text, trace_state text,
                queued_at timestamptz NOT NULL DEFAULT clock_timestamp(), available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                dispatched_at timestamptz, lease_token uuid, lease_until timestamptz,
                attempts bigint NOT NULL DEFAULT 0, revision bigint NOT NULL DEFAULT 0,
                failure_count bigint NOT NULL DEFAULT 0, failures_since_redrive bigint NOT NULL DEFAULT 0,
                last_failed_at timestamptz, last_failure_code varchar(128), dead_lettered_at timestamptz,
                redrive_count bigint NOT NULL DEFAULT 0, last_redriven_at timestamptz);
            INSERT INTO clockwork.outbox (message_id, destination, message_name, schema_version, payload, owner_key, correlation_id, causation_id, trace_parent, trace_state, queued_at)
                VALUES ('00000000-0000-0000-0000-000000000103', 'legacy-route', 'future.contract', 99,
                    '{"amount":17,"legacy":"v0"}', 'admitted', 'original-conversation', 'original-cause',
                    '00-0123456789abcdef0123456789abcdef-0123456789abcdef-01', 'legacy=value', '2034-03-15 09:00:00+00');
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP SCHEMA clockwork CASCADE");
}

[DbContext(typeof(TimerDatabase))]
[Migration("20261024000200_ImmutableDueIntents")]
sealed class ImmutableDueIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE clockwork.due_intents RENAME COLUMN "MessageId" TO message_id;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "Route" TO destination;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "Name" TO message_name;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "SchemaVersion" TO schema_version;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "Payload" TO payload;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "CorrelationId" TO correlation_id;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "CausationId" TO causation_id;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "TraceParent" TO trace_parent;
            ALTER TABLE clockwork.due_intents RENAME COLUMN "TraceState" TO trace_state;
            ALTER TABLE clockwork.due_intents ALTER COLUMN payload TYPE jsonb USING payload::jsonb;
            ALTER TABLE clockwork.due_intents ALTER COLUMN scope_key TYPE varchar(256);
            ALTER TABLE clockwork.due_intents ALTER COLUMN due_at TYPE timestamp(6) with time zone;
            ALTER TABLE clockwork.due_intents ADD COLUMN tenant_key text;
            UPDATE clockwork.due_intents SET tenant_key = scope_key;
            ALTER TABLE clockwork.due_intents ADD CONSTRAINT ck_due_intent_state CHECK (state IN (1,2,3));
            ALTER TABLE clockwork.due_intents ADD CONSTRAINT ck_due_intent_schema CHECK (schema_version > 0);
            CREATE UNIQUE INDEX ix_due_message ON clockwork.due_intents (message_id);
            CREATE INDEX ix_due_pending ON clockwork.due_intents (scope_key, due_at, schedule_id, occurrence_id) WHERE state = 1;
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Deploy a reviewed forward repair; never erase retained occurrence evidence."
        );
}
