using System.Security.Cryptography;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Select a disposable PostgreSQL connection.");
string name = "gloaming_recurrence_" + Guid.NewGuid().ToString("N");
string role = "gloaming_recurring_worker_" + Guid.NewGuid().ToString("N");
string password = Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync(token);

settings.Database = name;
var runtime = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{
    Username = role,
    Password = password,
};
Exception? failure = null;
try
{
    await using (var setup = Open(settings.ConnectionString))
    {
        await setup.GetService<IMigrator>().MigrateAsync("20261024010100_FiniteWork", token);
        Check(
            await setup
                .Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM clockwork.due_intents"
                )
                .SingleAsync(token) == 1,
            "Upgrade starts with retained finite data."
        );
        await setup.Database.MigrateAsync(token);
        Check(
            (await setup.Database.GetAppliedMigrationsAsync(token)).Count() == 2,
            "Both consumer-owned migrations must apply."
        );
        var retained = await setup.Set<DueIntentRecord>().SingleAsync(token);
        Check(
            retained.MessageId == Guid.Parse("00000000-0000-0000-0000-000000000088")
                && retained.DueAt == MaintenanceAdapter.Anchor.AddDays(1)
                && retained.State == DueIntentState.Pending
                && retained.TenantKey is null
                && retained.SchemaVersion == 99
                && retained.Payload.GetProperty("legacy").GetInt32() == 17,
            "Adding recurrence leaves existing finite identity, UTC instant and opaque payload unchanged."
        );
    }

    await using (var provision = new NpgsqlConnection(settings.ConnectionString))
    {
        await provision.OpenAsync(token);
        await using var grant = new NpgsqlCommand(
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CONNECT ON DATABASE {name} TO "{role}";
            GRANT USAGE ON SCHEMA clockwork TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON clockwork.recurring_intents, clockwork.due_intents TO "{role}";
            GRANT SELECT, INSERT ON clockwork.outbox TO "{role}";
            """,
            provision
        );
        await grant.ExecuteNonQueryAsync(token);
    }

    await using (var native = new NpgsqlConnection(runtime.ConnectionString))
    {
        await native.OpenAsync(token);
        await using var audit = new NpgsqlCommand(
            """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT has_schema_privilege(current_user, 'clockwork', 'CREATE')
                AND NOT has_table_privilege(current_user, 'clockwork.recurring_intents', 'DELETE')
                AND NOT has_table_privilege(current_user, 'clockwork.outbox', 'UPDATE')
                AND NOT pg_has_role(r.oid, (SELECT relowner FROM pg_class WHERE oid = 'clockwork.recurring_intents'::regclass), 'MEMBER')
            FROM pg_roles r WHERE rolname = current_user
            """,
            native
        );
        Check(
            await audit.ExecuteScalarAsync(token) is true,
            "Runtime grants must exclude migration, retention and outbox completion authority."
        );
        await using var denied = new NpgsqlCommand(
            "DELETE FROM clockwork.recurring_intents",
            native
        );
        try
        {
            await denied.ExecuteNonQueryAsync(token);
            throw new InvalidOperationException("Runtime retention deletion must fail.");
        }
        catch (PostgresException error)
            when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
    }

    var maintenance = MaintenanceAdapter.Definition;
    var growth = GrowthAdapter.Definition();
    await WriteAsync(database =>
    {
        var recurrence = new PostgresRecurringIntents<ScheduleDatabase>(database);
        recurrence.Register(MaintenanceAdapter.Key, maintenance);
        recurrence.Register(GrowthAdapter.Key, growth);
        return Task.FromResult(true);
    });
    Check(
        (
            await PlanAsync(
                MaintenanceAdapter.Key,
                maintenance,
                MaintenanceAdapter.Anchor,
                MaintenanceAdapter.Message
            )
        ).Staged == 1,
        "Maintenance arms one future occurrence."
    );
    Check(
        (
            await PlanAsync(
                MaintenanceAdapter.Key,
                maintenance,
                MaintenanceAdapter.Anchor,
                MaintenanceAdapter.Message
            )
        ).Staged == 0,
        "Repeated early polling cannot arm another future occurrence."
    );
    await using (var read = Open(runtime.ConnectionString))
    {
        var first = await read.Set<DueIntentRecord>()
            .SingleAsync(row => row.ScopeKey == "maintenance", token);
        // Independent Node SHA-256/UTC-tick calculation fixes the v1 identity protocol.
        Check(
            first.MessageId == Guid.Parse("d9e36c33-cdad-76b7-c7dc-27b1af089f87")
                && first.OccurrenceId == Guid.Parse("a8c130de-fc40-fc6b-1971-075f23057b7d")
                && first.ScheduleId == Guid.Parse("22409c99-f391-4b7f-97fb-c3fd557bc02a")
                && first.DueAt == MaintenanceAdapter.Anchor.AddMinutes(5)
                && first.TenantKey is null,
            "Retained complete identities and UTC instant must match literal v1 expectations."
        );
    }

    // An independent explicitly zoned recurring indication: a missed spring-gap occurrence
    // is coalesced to the real adjusted forward instant, then one future occurrence is armed.
    var planned = await PlanAsync(
        GrowthAdapter.Key,
        growth,
        new(2026, 3, 8, 7, 30, 0, TimeSpan.Zero),
        GrowthAdapter.Message
    );
    Check(
        planned == new RecurrencePlan(2, 1, false),
        "Zoned indication coalesces one elapsed occurrence and arms one future occurrence."
    );
    await using (var read = Open(runtime.ConnectionString))
    {
        var rows = await read.Set<DueIntentRecord>()
            .Where(row => row.ScopeKey == "growth")
            .OrderBy(row => row.DueAt)
            .ToArrayAsync(token);
        Check(
            rows.Select(row => row.DueAt)
                .SequenceEqual(
                    new DateTimeOffset[]
                    {
                        new(2026, 3, 8, 7, 0, 0, TimeSpan.Zero),
                        new(2026, 3, 9, 6, 30, 0, TimeSpan.Zero),
                    }
                ),
            "Spring-gap nominal instants must match independent UTC expectations."
        );
        Check(
            rows.All(row =>
                row.TenantKey == "selected-organization"
                && row.MessageName == "growth.review-indicated"
                && row.Payload.GetProperty("recipient").GetString() == "current-manager"
            ),
            "Indication policy is selected by its own consumer adapter."
        );
    }

    foreach (bool progress in new[] { true, false })
    {
        await using var database = Open(runtime.ConnectionString);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await new PostgresRecurringIntents<ScheduleDatabase>(database).PlanAsync(
            MaintenanceAdapter.Key,
            maintenance,
            MaintenanceAdapter.Anchor.AddMinutes(5),
            10,
            MaintenanceAdapter.Message,
            token
        );
        if (progress)
            database.ChangeTracker.Entries<RecurringIntentRecord>().Single().State =
                EntityState.Unchanged;
        else
            database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
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
        Check(rejected, "Restricted native guards must reject either omitted atomic participant.");
        await transaction.RollbackAsync(token);
    }

    await using (var database = Open(runtime.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await transaction.CreateSavepointAsync("before", token);
        await new PostgresRecurringIntents<ScheduleDatabase>(database).PlanAsync(
            MaintenanceAdapter.Key,
            maintenance,
            MaintenanceAdapter.Anchor.AddMinutes(5),
            10,
            MaintenanceAdapter.Message,
            token
        );
        await database.SaveChangesAsync(token);
        await transaction.RollbackToSavepointAsync("before", token);
        bool rejected = false;
        try
        {
            database.SaveChanges();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Check(rejected, "Savepoint-reverted progress is not genuine native stored evidence.");
        await transaction.RollbackAsync(token);
    }

    // Restore a literal owned v1 partial Coalesce cursor, independent of the implementation's serializer.
    var retainedKey = new RecurringIntentKey(
        "retained-coalesce",
        Guid.Parse("00000000-0000-0000-0000-000000000061"),
        Guid.Parse("00000000-0000-0000-0000-000000000062")
    );
    var retainedDefinition = RecurrenceDefinition.Every(
        "retained-v1",
        MaintenanceAdapter.Anchor,
        TimeSpan.FromMinutes(1),
        MissedOccurrences.Coalesce
    );
    await WriteAsync(database =>
    {
        new PostgresRecurringIntents<ScheduleDatabase>(database).Register(
            retainedKey,
            retainedDefinition
        );
        return Task.FromResult(true);
    });
    await using (var fixture = new NpgsqlConnection(settings.ConnectionString))
    {
        await fixture.OpenAsync(token);
        await using var install = new NpgsqlCommand(
            """
            UPDATE clockwork.recurring_intents SET revision = 2, progress =
            '{"Protocol":1,"Next":"2026-11-04T00:04:00+00:00","Armed":null,"Observed":"2026-11-04T00:12:00+00:00","Window":"2026-11-04T00:12:00+00:00","Candidate":"2026-11-04T00:03:00+00:00"}'
            WHERE scope_key = 'retained-coalesce'
            """,
            fixture
        );
        Check(
            await install.ExecuteNonQueryAsync(token) == 1,
            "Owned retained progress fixture installed once."
        );
    }

    var resumed = await PlanAsync(
        retainedKey,
        retainedDefinition,
        MaintenanceAdapter.Anchor.AddMinutes(20),
        MaintenanceAdapter.Message,
        9
    );
    Check(
        resumed == new RecurrencePlan(1, 9, true),
        "Retained window resumes through its original cutoff, not the newer clock."
    );
    await using (var read = Open(runtime.ConnectionString))
        Check(
            (
                await read.Set<DueIntentRecord>()
                    .SingleAsync(row => row.ScopeKey == retainedKey.ScopeKey, token)
            ).DueAt == MaintenanceAdapter.Anchor.AddMinutes(12),
            "No partial or later-window coalesced occurrence is substituted."
        );

    // Fresh successful false-save acceptance; restricted SELECT validates retained native participants.
    await using (var database = Open(runtime.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        Check(
            (
                await new PostgresRecurringIntents<ScheduleDatabase>(database).PlanAsync(
                    MaintenanceAdapter.Key,
                    maintenance,
                    MaintenanceAdapter.Anchor.AddMinutes(5),
                    10,
                    MaintenanceAdapter.Message,
                    token
                )
            ).Staged == 1,
            "Fresh recovery arms only the missing maintenance occurrence."
        );
        await database.SaveChangesAsync(false, token);
        database.ChangeTracker.AcceptAllChanges();
        Check(
            database.SaveChanges() == 0 && await database.SaveChangesAsync(token) == 0,
            "Genuine saved progress supports repeated native save forms."
        );
        await transaction.CommitAsync(token);
    }

    var stopped = await WriteAsync(database =>
        new PostgresRecurringIntents<ScheduleDatabase>(database).StopAsync(
            GrowthAdapter.Key,
            1,
            token
        )
    );
    Check(
        stopped == new RecurrenceStop(1, true),
        "Stopping remains bounded and disables planning immediately."
    );
    Check(
        (
            await PlanAsync(
                GrowthAdapter.Key,
                growth,
                new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                GrowthAdapter.Message
            )
        ).Staged == 0,
        "Stopped-in-progress generation cannot plan."
    );
    Check(
        await WriteAsync(database =>
            new PostgresRecurringIntents<ScheduleDatabase>(database).StopAsync(
                GrowthAdapter.Key,
                1,
                token
            )
        ) == new RecurrenceStop(1, false),
        "Fresh bounded stop completes suppression."
    );
    Console.WriteLine(
        "PASS recurrence: two consumers, populated finite upgrade, restricted grants, literal identities/cursor, atomic native recovery and bounded cancellation"
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
        Console.Error.WriteLine($"Recurrence cleanup failed ({error.GetType().Name}).");
    }
}

ScheduleDatabase Open(string connection) =>
    new(new DbContextOptionsBuilder<ScheduleDatabase>().UseNpgsql(connection).Options);

Task<RecurrencePlan> PlanAsync(
    RecurringIntentKey key,
    RecurrenceDefinition definition,
    DateTimeOffset observed,
    Func<RecurringOccurrence, OutgoingMessage> envelope,
    int budget = 100
) =>
    WriteAsync(database =>
        new PostgresRecurringIntents<ScheduleDatabase>(database).PlanAsync(
            key,
            definition,
            observed,
            budget,
            envelope,
            token
        )
    );

async Task<T> WriteAsync<T>(Func<ScheduleDatabase, Task<T>> operation)
{
    await using var database = Open(runtime.ConnectionString);
    await using var transaction = await database.Database.BeginTransactionAsync(token);
    T result = await operation(database);
    await database.SaveChangesAsync(token);
    await transaction.CommitAsync(token);
    return result;
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static class MaintenanceAdapter
{
    internal static readonly DateTimeOffset Anchor = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    internal static readonly RecurringIntentKey Key = new(
        "maintenance",
        Guid.Parse("00000000-0000-0000-0000-000000000051"),
        Guid.Parse("00000000-0000-0000-0000-000000000052")
    );
    internal static RecurrenceDefinition Definition =>
        RecurrenceDefinition.Every(
            "rotation-v1",
            Anchor,
            TimeSpan.FromMinutes(5),
            MissedOccurrences.Skip
        );

    internal static OutgoingMessage Message(RecurringOccurrence occurrence) =>
        new(
            occurrence.MessageId,
            "storage-worker",
            "maintenance.rotate",
            1,
            JsonSerializer.SerializeToElement(
                new { partition = "storage", nominal = occurrence.DueAt }
            )
        );
}

static class GrowthAdapter
{
    internal static readonly RecurringIntentKey Key = new(
        "growth",
        Guid.Parse("00000000-0000-0000-0000-000000000071"),
        Guid.Parse("00000000-0000-0000-0000-000000000072")
    );

    internal static RecurrenceDefinition Definition()
    {
        string directory = Environment.GetEnvironmentVariable("TZDIR") ?? "/usr/share/zoneinfo";
        string version = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "America", "New_York")))
        );
        return RecurrenceDefinition.Cron(
            "review-v1",
            new(2026, 3, 8, 0, 0, 0, TimeSpan.Zero),
            "30 2 * * *",
            RecurrenceCronFormat.FiveFields,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
            version,
            MissedOccurrences.Coalesce
        );
    }

    internal static OutgoingMessage Message(RecurringOccurrence occurrence) =>
        new(
            occurrence.MessageId,
            "growth-notices",
            "growth.review-indicated",
            2,
            JsonSerializer.SerializeToElement(
                new { recipient = "current-manager", nominal = occurrence.DueAt }
            ),
            "selected-organization"
        );
}

sealed class ScheduleDatabase(DbContextOptions<ScheduleDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigurePostgresOutbox("clockwork", "outbox");
        model.ConfigurePostgresDueIntents("clockwork");
        model.ConfigurePostgresRecurrence("clockwork");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateRecurrenceChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        await this.ValidateRecurrenceChangesAsync(cancellationToken);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

[DbContext(typeof(ScheduleDatabase))]
[Migration("20261024010100_FiniteWork")]
sealed class FiniteWork : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE SCHEMA clockwork;
            CREATE TABLE clockwork.due_intents (
                scope_key varchar(256) COLLATE "C" NOT NULL, schedule_id uuid NOT NULL, occurrence_id uuid NOT NULL,
                due_at timestamp(6) with time zone NOT NULL, state integer NOT NULL CHECK (state IN (1,2,3)),
                message_id uuid NOT NULL UNIQUE, destination text NOT NULL, message_name text NOT NULL,
                schema_version integer NOT NULL CHECK (schema_version > 0), payload jsonb NOT NULL,
                tenant_key text, correlation_id text, causation_id text, trace_parent text, trace_state text,
                PRIMARY KEY (scope_key, schedule_id, occurrence_id));
            CREATE INDEX ix_due_pending ON clockwork.due_intents (scope_key, due_at, schedule_id, occurrence_id) WHERE state = 1;
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
            INSERT INTO clockwork.due_intents (scope_key, schedule_id, occurrence_id, due_at, state, message_id, destination, message_name, schema_version, payload)
                VALUES ('legacy', '00000000-0000-0000-0000-000000000081', '00000000-0000-0000-0000-000000000082',
                    '2026-11-05T00:00:00Z', 1, '00000000-0000-0000-0000-000000000088', 'legacy-route', 'future.contract', 99, '{"legacy":17}');
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Use a reviewed forward repair; retain finite identity evidence."
        );
}

[DbContext(typeof(ScheduleDatabase))]
[Migration("20261024010200_RecurringGenerations")]
sealed class RecurringGenerations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE TABLE clockwork.recurring_intents (
                scope_key varchar(256) COLLATE "C" NOT NULL, schedule_id uuid NOT NULL, generation_id uuid NOT NULL,
                definition text NOT NULL, progress text NOT NULL, revision bigint NOT NULL CHECK (revision > 0),
                state integer NOT NULL CHECK (state IN (1,2,3)), PRIMARY KEY (scope_key, schedule_id, generation_id));
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Use a reviewed forward repair; retain recurrence generation evidence."
        );
}
