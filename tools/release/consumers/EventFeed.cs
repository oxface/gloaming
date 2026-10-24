using System.Text;
using Gloaming.EventSourcing.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Select disposable PostgreSQL credentials.");
string suffix = Guid.NewGuid().ToString("N");
string databaseName = "gloaming_feed_" + suffix;
string owner = "feed_owner_" + suffix;
string writer = "feed_writer_" + suffix;
string worker = "feed_worker_" + suffix;
string password = Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
    await create.ExecuteNonQueryAsync(token);

settings.Database = databaseName;
var writerSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{
    Username = writer,
    Password = password,
};
var workerSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{
    Username = worker,
    Password = password,
};
Exception? failure = null;
try
{
    await using (var native = new NpgsqlConnection(settings.ConnectionString))
    {
        await native.OpenAsync(token);
        await SqlAsync(
            native,
            $"""
            CREATE ROLE {owner} NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            CREATE ROLE {writer} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            CREATE ROLE {worker} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CREATE ON DATABASE {databaseName} TO {owner};
            GRANT CREATE ON SCHEMA public TO {owner};
            SET ROLE {owner};
            """,
            token
        );
        await using (
            var migrations = new AdoptionDatabase(
                new DbContextOptionsBuilder<AdoptionDatabase>().UseNpgsql(native).Options
            )
        )
        {
            await migrations.GetService<IMigrator>().MigrateAsync("20261024030100_Journals", token);
            Check(
                await CountAsync(native, "SELECT count(*) FROM own.accounts", token) == 1,
                "Upgrade begins with populated native history."
            );
            await migrations.Database.MigrateAsync(token);
            Check(
                (await migrations.Database.GetAppliedMigrationsAsync(token)).Count() == 2,
                "Both owned migrations applied under restricted owner."
            );
        }

        await SqlAsync(
            native,
            $"""
            RESET ROLE;
            GRANT CONNECT ON DATABASE {databaseName} TO {writer}, {worker};
            GRANT USAGE ON SCHEMA own TO {writer}, {worker};
            GRANT USAGE ON SCHEMA feed TO {worker};
            GRANT INSERT ON own.accounts, own.audit TO {writer};
            GRANT SELECT ON feed.feed_head, feed.feed_entries TO {worker};
            GRANT UPDATE(singleton) ON feed.feed_head TO {worker};
            GRANT SELECT, INSERT, UPDATE ON feed.projection_checkpoints, feed.projection_active, own.views TO {worker};
            """,
            token
        );
        Check(
            await CountAsync(
                native,
                $"""
                SELECT count(*) FROM pg_proc p JOIN pg_roles r ON r.oid = p.proowner
                WHERE p.pronamespace = 'feed'::regnamespace AND p.proname IN ('capture_0', 'capture_1')
                    AND r.rolname = '{owner}' AND NOT r.rolsuper AND NOT r.rolbypassrls
                    AND p.prosecdef AND p.proconfig = ARRAY['search_path=pg_catalog, pg_temp']::text[]
                """,
                token
            ) == 2,
            "Both capture functions have the audited restricted owner and fixed trusted path."
        );
    }

    await using var services = new ServiceCollection()
        .AddDbContext<AdoptionDatabase>(options =>
            options.UseNpgsql(workerSettings.ConnectionString)
        )
        .BuildServiceProvider();
    var feed = new PostgresEventFeed<AdoptionDatabase>(
        services.GetRequiredService<IServiceScopeFactory>(),
        Protocol.Location
    );
    var limits = new FeedLimits(32, 1024 * 1024);
    var first = await feed.ReadAsync(FeedCursor.Start(Protocol.Location.FeedId), limits, token);
    Check(
        first.Events.Count == 3 && first.ObservedHead == 1,
        "Populated baseline includes every complete source key."
    );
    var retained = first.Events.Single(x => x.Source == "accounts");
    Check(
        retained.Envelope.GetProperty("schema").GetInt32() == 99
            && retained.Envelope.GetProperty("body").GetProperty("future").GetString() == "retained"
            && retained.EventKey[0].GetGuid() == Guid.Parse("00000000-0000-0000-0000-000000000091"),
        "Original native wire content/identity survives bootstrap."
    );

    // Literal v1 cursor is independent of the current writer serializer, including native key spacing.
    string literal =
        """{"Protocol":1,"FeedId":"00000000-0000-0000-0000-000000000081","Position":1,"Source":"audit","Stream":"[\"Owner\", 7]","Version":1,"Event":"[\"Owner\", 1]"}""";
    string literalCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(literal));
    var partial = FeedCursor.Parse(literalCursor);
    Check(partial.Value == literalCursor, "Retained cursor bytes survive reader restoration.");
    var rest = await feed.ReadAsync(partial, new(1, 1024 * 1024), token);
    Check(
        rest.Events.Count == 1
            && rest.Events[0].StreamKey[0].GetString() == "owner"
            && !rest.HasMore,
        "Literal retained intra-batch cursor preserves complete case-sensitive keys."
    );

    await using (var seed = new NpgsqlConnection(settings.ConnectionString))
    {
        await seed.OpenAsync(token);
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO feed.projection_checkpoints VALUES('counts', 'v1', 'count-v1', @cursor);
            INSERT INTO own.views VALUES('counts', 'v1', 'Owner', 7, 1)
            """,
            seed
        );
        insert.Parameters.AddWithValue("cursor", literalCursor);
        await insert.ExecuteNonQueryAsync(token);
    }

    Check(
        (await feed.ProjectNextAsync(Counts("v1"), limits, token)).Status
            == ProjectionStatus.Applied,
        "Resume literal retained partial checkpoint."
    );
    Check(
        (await feed.ProjectNextAsync(Accounts("v1"), limits, token)).Status
            == ProjectionStatus.Applied,
        "Independent account reducer accepts original schema99."
    );
    Check(
        (await feed.ProjectNextAsync(Accounts("v1"), limits, token)).Status
            == ProjectionStatus.NoWork,
        "Repeated turn cannot apply facts twice."
    );
    await ValuesAsync(services, "counts", "v1", [1, 1], token);
    await ValuesAsync(services, "balances", "v1", [17], token);

    await using (var native = new NpgsqlConnection(workerSettings.ConnectionString))
    {
        await native.OpenAsync(token);
        Check(
            await CountAsync(
                native,
                """
                SELECT CASE WHEN NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                    AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                    AND NOT has_schema_privilege(current_user, 'feed', 'CREATE')
                    AND NOT has_table_privilege(current_user, 'feed.feed_entries', 'INSERT')
                    AND NOT has_table_privilege(current_user, 'own.accounts', 'INSERT')
                    AND has_column_privilege(current_user, 'feed.feed_head', 'singleton', 'UPDATE')
                    AND NOT has_column_privilege(current_user, 'feed.feed_head', 'position', 'UPDATE')
                    THEN 1 ELSE 0 END FROM pg_roles r WHERE rolname = current_user
                """,
                token
            ) == 1,
            "Runtime grants exclude capture ownership and head/copy mutation."
        );
        await DeniedAsync(native, "UPDATE feed.feed_head SET position = 9000", token);
        await DeniedAsync(native, "DELETE FROM feed.feed_entries", token);
    }

    await using (var native = new NpgsqlConnection(writerSettings.ConnectionString))
    {
        await native.OpenAsync(token);
        // No EXECUTE privilege on the owner function and no feed schema/table rights are required by the trigger.
        await SqlAsync(
            native,
            """
            INSERT INTO own.accounts VALUES('00000000-0000-0000-0000-000000000092', 9, 2, 'account.adjusted', 1, '{"amount":5}')
            """,
            token
        );
        await DeniedAsync(native, "DELETE FROM own.accounts", token);
    }

    Check(
        (await feed.ProjectNextAsync(Accounts("v1"), limits, token)).Status
            == ProjectionStatus.Applied,
        "Old native INSERT is captured with restricted grants."
    );
    Check(
        (await feed.ActivateAsync("balances", "v1", null, token)).Status
            == ProjectionStatus.Activated,
        "Restricted singleton-column grant permits native activation."
    );
    await ValuesAsync(services, "balances", "v1", [22], token);
    FeedPage? page;
    do
    {
        var turn = await feed.ProjectNextAsync(Accounts("v2"), new(1, 1024 * 1024), token);
        Check(
            turn.Status == ProjectionStatus.Applied,
            "Bounded secondary generation applies its next page."
        );
        page = turn.Page;
    } while (page!.HasMore);

    Check(
        (await feed.ActivateAsync("balances", "v2", null, token)).Status
            == ProjectionStatus.Conflict,
        "Stale operator pointer cannot select a generation."
    );
    Check(
        (await feed.ActivateAsync("balances", "v2", "v1", token)).Status
            == ProjectionStatus.Activated,
        "Complete new generation switches atomically."
    );
    await using (var native = new NpgsqlConnection(workerSettings.ConnectionString))
    {
        await native.OpenAsync(token);
        Check(
            await CountAsync(
                native,
                "SELECT v.value FROM own.views v JOIN feed.projection_active a ON a.name = v.projection AND a.generation = v.generation WHERE a.name = 'balances'",
                token
            ) == 22,
            "One native reader statement observes the selected complete output."
        );
    }

    Console.WriteLine(
        "PASS event-feed: populated owned upgrade, literal v1 continuation, native old writer, restricted definer capture, two reducers and generation cutover"
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
        await using (
            var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin)
        )
            await drop.ExecuteNonQueryAsync(cleanup.Token);

        foreach (string role in new[] { worker, writer, owner })
        {
            await using var dropRole = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", admin);
            await dropRole.ExecuteNonQueryAsync(cleanup.Token);
        }
    }
    catch (Exception error) when (failure is not null)
    {
        Console.Error.WriteLine($"Feed cleanup failed ({error.GetType().Name}).");
    }
}

static ProjectionGeneration<AdoptionDatabase> Accounts(string generation) =>
    new(
        "balances",
        generation,
        "balances-v1",
        async (db, selected, page, ct) =>
        {
            foreach (var fact in page.Events.Where(x => x.Source == "accounts"))
            {
                Check(
                    fact.Envelope.GetProperty("kind").GetString() == "account.adjusted"
                        && fact.Envelope.GetProperty("schema").GetInt32() is 1 or 99,
                    "Consumer owns historical reader compatibility."
                );
                long subject = fact.StreamKey[0].GetInt64();
                var row =
                    db.Set<View>()
                        .Local.SingleOrDefault(x =>
                            x.Projection == "balances"
                            && x.Generation == selected
                            && x.Subject == subject
                        )
                    ?? await db.Set<View>()
                        .SingleOrDefaultAsync(
                            x =>
                                x.Projection == "balances"
                                && x.Generation == selected
                                && x.Subject == subject,
                            ct
                        );
                if (row is null)
                {
                    row = new()
                    {
                        Projection = "balances",
                        Generation = selected,
                        Owner = "",
                        Subject = subject,
                    };
                    db.Add(row);
                }

                row.Value += fact.Envelope.GetProperty("body").GetProperty("amount").GetInt32();
            }
        }
    );

static ProjectionGeneration<AdoptionDatabase> Counts(string generation) =>
    new(
        "counts",
        generation,
        "count-v1",
        async (db, selected, page, ct) =>
        {
            foreach (var fact in page.Events.Where(x => x.Source == "audit"))
            {
                Check(
                    fact.Envelope.GetProperty("kind").GetString() == "owner.action",
                    "Consumer chooses countable facts."
                );
                string scope = fact.StreamKey[0].GetString()!;
                long subject = fact.StreamKey[1].GetInt64();
                var row =
                    db.Set<View>()
                        .Local.SingleOrDefault(x =>
                            x.Projection == "counts"
                            && x.Generation == selected
                            && x.Owner == scope
                            && x.Subject == subject
                        )
                    ?? await db.Set<View>()
                        .SingleOrDefaultAsync(
                            x =>
                                x.Projection == "counts"
                                && x.Generation == selected
                                && x.Owner == scope
                                && x.Subject == subject,
                            ct
                        );
                if (row is null)
                {
                    row = new()
                    {
                        Projection = "counts",
                        Generation = selected,
                        Owner = scope,
                        Subject = subject,
                    };
                    db.Add(row);
                }

                row.Value++;
            }
        }
    );

static async Task ValuesAsync(
    ServiceProvider services,
    string projection,
    string generation,
    int[] expected,
    CancellationToken token
)
{
    await using var scope = services.CreateAsyncScope();
    int[] actual = await scope
        .ServiceProvider.GetRequiredService<AdoptionDatabase>()
        .Set<View>()
        .Where(x => x.Projection == projection && x.Generation == generation)
        .OrderBy(x => x.Value)
        .Select(x => x.Value)
        .ToArrayAsync(token);
    Check(actual.SequenceEqual(expected), "Independent expected projection values differ.");
}

static async Task SqlAsync(NpgsqlConnection connection, string sql, CancellationToken token)
{
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync(token);
}

static async Task<long> CountAsync(NpgsqlConnection connection, string sql, CancellationToken token)
{
    await using var command = new NpgsqlCommand(sql, connection);
    return Convert.ToInt64(
        await command.ExecuteScalarAsync(token),
        System.Globalization.CultureInfo.InvariantCulture
    );
}

static async Task DeniedAsync(NpgsqlConnection connection, string sql, CancellationToken token)
{
    try
    {
        await SqlAsync(connection, sql, token);
        throw new InvalidOperationException("Restricted runtime mutation unexpectedly succeeded.");
    }
    catch (PostgresException error)
        when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static class Protocol
{
    internal static readonly EventFeedLocation Location = new(
        "feed",
        Guid.Parse("00000000-0000-0000-0000-000000000081")
    );
    internal static readonly EventFeedSource[] Sources =
    [
        new("accounts", "own", "accounts", ["account"], "version", ["id"]),
        new("audit", "own", "audit", ["owner", "subject"], "version", ["owner", "id"]),
    ];
}

sealed class AdoptionDatabase(DbContextOptions<AdoptionDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        var row = model.Entity<View>();
        row.ToTable("views", "own");
        row.HasKey(x => new
        {
            x.Projection,
            x.Generation,
            x.Owner,
            x.Subject,
        });
        row.Property(x => x.Projection).HasColumnName("projection");
        row.Property(x => x.Generation).HasColumnName("generation");
        row.Property(x => x.Owner).HasColumnName("owner");
        row.Property(x => x.Subject).HasColumnName("subject");
        row.Property(x => x.Value).HasColumnName("value");
    }
}

sealed class View
{
    public string Projection { get; set; } = null!;
    public string Generation { get; set; } = null!;
    public string Owner { get; set; } = null!;
    public long Subject { get; set; }
    public int Value { get; set; }
}

[DbContext(typeof(AdoptionDatabase))]
[Migration("20261024030100_Journals")]
sealed class Journals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE SCHEMA own; CREATE SCHEMA feed;
            CREATE TABLE own.accounts(id uuid PRIMARY KEY, account bigint NOT NULL, version integer NOT NULL,
                kind text NOT NULL, schema integer NOT NULL, body jsonb NOT NULL, UNIQUE(account, version));
            CREATE TABLE own.audit(owner text COLLATE "C" NOT NULL, id bigint NOT NULL, subject bigint NOT NULL, version bigint NOT NULL,
                kind text NOT NULL, schema integer NOT NULL, body jsonb NOT NULL, PRIMARY KEY(owner, id), UNIQUE(owner, subject, version));
            CREATE TABLE own.views(projection text COLLATE "C" NOT NULL, generation text COLLATE "C" NOT NULL,
                owner text COLLATE "C" NOT NULL, subject bigint NOT NULL, value integer NOT NULL,
                PRIMARY KEY(projection, generation, owner, subject));
            INSERT INTO own.accounts VALUES('00000000-0000-0000-0000-000000000091', 9, 1, 'account.adjusted', 99, '{"amount":17,"future":"retained"}');
            INSERT INTO own.audit VALUES('Owner', 1, 7, 1, 'owner.action', 1, '{}'), ('owner', 1, 7, 1, 'owner.action', 1, '{}');
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retain owned facts; use reviewed forward repair.");
}

[DbContext(typeof(AdoptionDatabase))]
[Migration("20261024030200_CommittedFeed")]
sealed class CommittedFeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EventFeedInstallation.Create(Protocol.Location, Protocol.Sources));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retain feed/cursors; use reviewed forward repair.");
}
