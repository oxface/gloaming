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
    ?? throw new InvalidOperationException("Select a disposable PostgreSQL connection.");
string name = "gloaming_contributions_" + Guid.NewGuid().ToString("N");
string role = "gloaming_join_worker_" + Guid.NewGuid().ToString("N");
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
var key = new ContributionSetKey(
    "independent",
    Guid.Parse("00000000-0000-0000-0000-000000000081"),
    Guid.Parse("00000000-0000-0000-0000-000000000082")
);
var due = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
Guid completeId = Guid.Parse("00000000-0000-0000-0000-000000000101");
Guid signalId = Guid.Parse("00000000-0000-0000-0000-000000000102");
Guid timeoutId = Guid.Parse("00000000-0000-0000-0000-000000000103");
Exception? failure = null;
try
{
    await using (var setup = Open(settings.ConnectionString))
    {
        await setup.GetService<IMigrator>().MigrateAsync("20261024020100_FiniteJournal", token);
        Check(
            await setup.Set<DueIntentRecord>().CountAsync(token) == 1,
            "Upgrade must start from populated finite data."
        );
        await setup.Database.MigrateAsync(token);
        Check(
            (await setup.Database.GetAppliedMigrationsAsync(token)).Count() == 2,
            "Both owned native migrations apply."
        );
        var retained = await setup.Set<DueIntentRecord>().SingleAsync(token);
        Check(
            retained.MessageId == Guid.Parse("00000000-0000-0000-0000-000000000088")
                && retained.DueAt == due
                && retained.Payload.GetProperty("retained").GetInt32() == 17
                && retained.SchemaVersion == 99,
            "Adding contributions preserves retained finite identity/time/opaque content."
        );
    }

    await using (var provision = new NpgsqlConnection(settings.ConnectionString))
    {
        await provision.OpenAsync(token);
        await using var grant = new NpgsqlCommand(
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CONNECT ON DATABASE {name} TO "{role}";
            GRANT USAGE ON SCHEMA joining TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON joining.contribution_sets, joining.due_intents TO "{role}";
            GRANT SELECT, INSERT ON joining.contributions, joining.outbox TO "{role}";
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
                AND NOT has_schema_privilege(current_user, 'joining', 'CREATE')
                AND NOT has_table_privilege(current_user, 'joining.contribution_sets', 'DELETE')
                AND NOT has_table_privilege(current_user, 'joining.contributions', 'UPDATE')
                AND NOT has_table_privilege(current_user, 'joining.contributions', 'DELETE')
                AND NOT has_table_privilege(current_user, 'joining.outbox', 'UPDATE')
            FROM pg_roles r WHERE rolname = current_user
            """,
            native
        );
        Check(
            await audit.ExecuteScalarAsync(token) is true,
            "Runtime grants must exclude migration, retention and content mutation."
        );
        await using var denied = new NpgsqlCommand(
            "UPDATE joining.contributions SET name = 'changed'",
            native
        );
        try
        {
            await denied.ExecuteNonQueryAsync(token);
            throw new InvalidOperationException("Immutable content UPDATE must be denied.");
        }
        catch (PostgresException error)
            when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
    }

    await WriteAsync(database =>
    {
        new PostgresContributionSets<JoinDatabase>(database).Register(
            key,
            new(
                ["part-1", "part-2", "part-3"],
                2,
                Notice(completeId, "technical.ready"),
                new(
                    due,
                    Notice(signalId, "technical.timeout-signal"),
                    Notice(timeoutId, "technical.timed-out")
                )
            )
        );
        return Task.FromResult(true);
    });

    // Owned literal v1 partial state, independent of the current serializer. This is an
    // initial-protocol fixture, not a claim of two released Gloaming library versions.
    await using (var earlier = new NpgsqlConnection(settings.ConnectionString))
    {
        await earlier.OpenAsync(token);
        await using var seed = new NpgsqlCommand(
            """
            UPDATE joining.contribution_sets SET revision = 2,
                expected = '{"Protocol":1,"Identities":["part-1","part-2","part-3"],"Required":2}',
                completion = '{"Protocol":1,"Message":{"MessageId":"00000000-0000-0000-0000-000000000101","RouteKey":"selected-route","MessageName":"technical.ready","SchemaVersion":99,"Payload":{"retained":17,"future":"opaque"},"TenantKey":null,"CorrelationId":"conversation","CausationId":"cause","TraceParent":"00-0123456789abcdef0123456789abcdef-0123456789abcdef-01","TraceState":"vendor=old"}}'
                WHERE scope_key = 'independent';
            INSERT INTO joining.contributions (scope_key, work_id, generation_id, contributor_id, name, schema_version, payload)
                VALUES ('independent', '00000000-0000-0000-0000-000000000081', '00000000-0000-0000-0000-000000000082',
                    'part-1', 'owned.result', 7, '{"amount":17,"from":"earlier-v1"}');
            """,
            earlier
        );
        await seed.ExecuteNonQueryAsync(token);
    }

    Check(
        await AddAsync("part-1", Content(17)) == ContributionResult.Duplicate,
        "Literal retained content must compare equal without another effect."
    );
    bool conflict = false;
    try
    {
        await AddAsync("part-1", Content(18));
    }
    catch (ContributionConflictException)
    {
        conflict = true;
    }
    Check(conflict, "Changed retained content must conflict.");

    foreach (string participant in new[] { "set", "content", "outbox", "timer", "original" })
    {
        await using var database = Open(runtime.ConnectionString);
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await new PostgresContributionSets<JoinDatabase>(database).AddAsync(
            key,
            "part-2",
            Content(23),
            token
        );
        if (participant == "set")
            database.ChangeTracker.Entries<ContributionSetRecord>().Single().State =
                EntityState.Unchanged;
        else if (participant == "content")
            database.ChangeTracker.Entries<ContributionRecord>().Single().State =
                EntityState.Unchanged;
        else if (participant == "outbox")
            database.ChangeTracker.Entries<OutboxMessageRecord>().Single().State =
                EntityState.Unchanged;
        else if (participant == "timer")
            database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
                EntityState.Unchanged;
        else
        {
            var state = database
                .ChangeTracker.Entries<ContributionSetRecord>()
                .Single()
                .Property(row => row.State);
            state.OriginalValue = state.CurrentValue;
        }

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
            "Restricted native guards must reject omitted/changed original participants."
        );
        await transaction.RollbackAsync(token);
    }

    await using (var database = Open(runtime.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await transaction.CreateSavepointAsync("before", token);
        await new PostgresContributionSets<JoinDatabase>(database).AddAsync(
            key,
            "part-2",
            Content(23),
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
        Check(rejected, "A reverted native completion is not stored participation.");
        await transaction.RollbackAsync(token);
    }

    await using (var database = Open(runtime.ConnectionString))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await new PostgresContributionSets<JoinDatabase>(database).AddAsync(
            key,
            "part-2",
            Content(23),
            token
        );
        await database.SaveChangesAsync(false, token);
        database.ChangeTracker.AcceptAllChanges();
        Check(
            database.SaveChanges() == 0 && await database.SaveChangesAsync(token) == 0,
            "Genuine stored JSONB participants support explicit acceptance and both repeated save forms."
        );
        await transaction.CommitAsync(token);
    }

    Check(
        await AddAsync("part-2", Content(23)) == ContributionResult.Duplicate,
        "Equal completion retry must not enqueue again."
    );
    Check(
        await AddAsync("part-3", Content(31)) == ContributionResult.AlreadyCompleted,
        "New late content must not reopen a closed generation."
    );
    await using (var read = Open(runtime.ConnectionString))
    {
        Check(
            await read.Set<ContributionRecord>().CountAsync(token) == 2,
            "Only selected pre-closure content remains."
        );
        var ready = await read.Set<OutboxMessageRecord>().SingleAsync(token);
        Check(
            ready.MessageId == completeId
                && ready.SchemaVersion == 99
                && ready.TenantKey is null
                && ready.Payload.GetProperty("future").GetString() == "opaque"
                && ready.Payload.GetProperty("retained").GetInt32() == 17
                && ready.CorrelationId == "conversation"
                && ready.CausationId == "cause"
                && ready.TraceParent == "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01"
                && ready.TraceState == "vendor=old",
            "Literal complete original wire envelope survives native completion unchanged in value."
        );
        Check(
            (
                await read.Set<DueIntentRecord>()
                    .SingleAsync(row => row.ScopeKey == key.ScopeKey, token)
            ).State == DueIntentState.Cancelled,
            "Completion suppresses the original finite timeout signal."
        );
    }

    var timeoutKey = new ContributionSetKey(
        "timeout",
        key.WorkId,
        Guid.Parse("00000000-0000-0000-0000-000000000083")
    );
    Guid timeoutNotice = Guid.Parse("00000000-0000-0000-0000-000000000106");
    await WriteAsync(database =>
    {
        new PostgresContributionSets<JoinDatabase>(database).Register(
            timeoutKey,
            new(
                ["one", "two"],
                2,
                Notice(Guid.Parse("00000000-0000-0000-0000-000000000104"), "timeout.ready"),
                new(
                    due,
                    Notice(Guid.Parse("00000000-0000-0000-0000-000000000105"), "timeout.signal"),
                    Notice(timeoutNotice, "timeout.closed")
                )
            )
        );
        return Task.FromResult(true);
    });
    Check(
        await WriteAsync(database =>
            new PostgresContributionSets<JoinDatabase>(database).TimeoutAsync(
                timeoutKey,
                due.AddTicks(-10),
                token
            )
        ) == ContributionTimeoutResult.NotDue,
        "Timeout eligibility is exact to one microsecond."
    );
    Check(
        await WriteAsync(database =>
            new PostgresContributionSets<JoinDatabase>(database).TimeoutAsync(
                timeoutKey,
                due,
                token
            )
        ) == ContributionTimeoutResult.TimedOut,
        "Eligible timeout stages one selected outcome."
    );
    Check(
        await WriteAsync(database =>
            new PostgresContributionSets<JoinDatabase>(database).TimeoutAsync(
                timeoutKey,
                due,
                token
            )
        ) == ContributionTimeoutResult.AlreadyTimedOut,
        "Repeated timeout cannot close twice."
    );
    await using (var read = Open(runtime.ConnectionString))
        Check(
            await read.Set<OutboxMessageRecord>().CountAsync(token) == 2
                && await read.Set<OutboxMessageRecord>()
                    .AnyAsync(row => row.MessageId == timeoutNotice, token),
            "Completion and separate timeout each retain exactly one outcome."
        );

    using var ambiguous = JsonDocument.Parse("{\"nested\":[{\"x\":1,\"x\":2}]}");
    bool ambiguousRejected = false;
    try
    {
        _ = new ContributionValue("owned.result", 7, ambiguous.RootElement);
    }
    catch (ArgumentException)
    {
        ambiguousRejected = true;
    }
    Check(ambiguousRejected, "Ambiguous JSON cannot become unrepeatable native content.");
    Console.WriteLine(
        "PASS contributions: populated upgrade, literal retained v1 set/content/envelope, restricted immutable grants, atomic participant/native recovery and timeout"
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
        Console.Error.WriteLine($"Contribution cleanup failed ({error.GetType().Name}).");
    }
}

JoinDatabase Open(string connection) =>
    new(new DbContextOptionsBuilder<JoinDatabase>().UseNpgsql(connection).Options);
Task<ContributionResult> AddAsync(string contributor, ContributionValue value) =>
    WriteAsync(database =>
        new PostgresContributionSets<JoinDatabase>(database).AddAsync(
            key,
            contributor,
            value,
            token
        )
    );

async Task<T> WriteAsync<T>(Func<JoinDatabase, Task<T>> operation)
{
    await using var database = Open(runtime.ConnectionString);
    await using var transaction = await database.Database.BeginTransactionAsync(token);
    T result = await operation(database);
    await database.SaveChangesAsync(token);
    await transaction.CommitAsync(token);
    return result;
}

static ContributionValue Content(int amount) =>
    new("owned.result", 7, JsonSerializer.SerializeToElement(new { amount, from = "earlier-v1" }));
static OutgoingMessage Notice(Guid id, string name) =>
    new(
        id,
        "selected-route",
        name,
        99,
        JsonSerializer.SerializeToElement(new { retained = 17, future = "opaque" }),
        correlationId: "conversation",
        causationId: "cause",
        traceParent: "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
        traceState: "vendor=old"
    );

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class JoinDatabase(DbContextOptions<JoinDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigurePostgresOutbox("joining", "outbox");
        model.ConfigurePostgresDueIntents("joining");
        model.ConfigurePostgresContributions("joining");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateContributionChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        await this.ValidateContributionChangesAsync(cancellationToken);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

[DbContext(typeof(JoinDatabase))]
[Migration("20261024020100_FiniteJournal")]
sealed class FiniteJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE SCHEMA joining;
            CREATE TABLE joining.due_intents (
                scope_key varchar(256) COLLATE "C" NOT NULL, schedule_id uuid NOT NULL, occurrence_id uuid NOT NULL,
                due_at timestamp(6) with time zone NOT NULL, state integer NOT NULL CHECK (state IN (1,2,3)),
                message_id uuid NOT NULL UNIQUE, destination text NOT NULL, message_name text NOT NULL,
                schema_version integer NOT NULL CHECK (schema_version > 0), payload jsonb NOT NULL,
                tenant_key text, correlation_id text, causation_id text, trace_parent text, trace_state text,
                PRIMARY KEY (scope_key, schedule_id, occurrence_id));
            CREATE INDEX ix_due_pending ON joining.due_intents (scope_key, due_at, schedule_id, occurrence_id) WHERE state = 1;
            CREATE TABLE joining.outbox (
                message_id uuid PRIMARY KEY, destination text NOT NULL, message_name text NOT NULL,
                schema_version integer NOT NULL, payload jsonb NOT NULL, owner_key text,
                correlation_id text, causation_id text, trace_parent text, trace_state text,
                queued_at timestamptz NOT NULL DEFAULT clock_timestamp(), available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                dispatched_at timestamptz, lease_token uuid, lease_until timestamptz,
                attempts bigint NOT NULL DEFAULT 0, revision bigint NOT NULL DEFAULT 0,
                failure_count bigint NOT NULL DEFAULT 0, failures_since_redrive bigint NOT NULL DEFAULT 0,
                last_failed_at timestamptz, last_failure_code varchar(128), dead_lettered_at timestamptz,
                redrive_count bigint NOT NULL DEFAULT 0, last_redriven_at timestamptz);
            INSERT INTO joining.due_intents (scope_key, schedule_id, occurrence_id, due_at, state, message_id, destination, message_name, schema_version, payload)
                VALUES ('legacy', '00000000-0000-0000-0000-000000000084', '00000000-0000-0000-0000-000000000085',
                    '2026-11-05T00:00:00Z', 1, '00000000-0000-0000-0000-000000000088', 'legacy-route', 'future.contract', 99, '{"retained":17}');
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Use a reviewed forward repair; retain finite evidence.");
}

[DbContext(typeof(JoinDatabase))]
[Migration("20261024020200_ContributionSets")]
sealed class ContributionSets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE TABLE joining.contribution_sets (
                scope_key varchar(256) COLLATE "C" NOT NULL, work_id uuid NOT NULL, generation_id uuid NOT NULL,
                expected jsonb NOT NULL, completion jsonb NOT NULL, due_at timestamp(6) with time zone,
                timeout_signal jsonb, timeout_outcome jsonb, revision bigint NOT NULL CHECK (revision > 0),
                state integer NOT NULL CHECK (state IN (1,2,3)), PRIMARY KEY (scope_key, work_id, generation_id),
                CHECK ((due_at IS NULL AND timeout_signal IS NULL AND timeout_outcome IS NULL)
                    OR (due_at IS NOT NULL AND timeout_signal IS NOT NULL AND timeout_outcome IS NOT NULL)));
            CREATE TABLE joining.contributions (
                scope_key varchar(256) COLLATE "C" NOT NULL, work_id uuid NOT NULL, generation_id uuid NOT NULL,
                contributor_id varchar(256) COLLATE "C" NOT NULL, name text NOT NULL, schema_version integer NOT NULL CHECK (schema_version > 0), payload jsonb NOT NULL,
                PRIMARY KEY (scope_key, work_id, generation_id, contributor_id),
                FOREIGN KEY (scope_key, work_id, generation_id) REFERENCES joining.contribution_sets (scope_key, work_id, generation_id) ON DELETE RESTRICT);
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Use a reviewed forward repair; retain generation/content evidence."
        );
}
