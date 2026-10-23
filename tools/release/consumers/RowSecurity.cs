using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "A disposable PostgreSQL server with CREATEDB and role-provisioning permission is required."
    );

string name = "gloaming_rls_" + Guid.NewGuid().ToString("N");
string role = "gloaming_rls_login_" + Guid.NewGuid().ToString("N");
string password = Guid.NewGuid().ToString("N");
var adminSettings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync(token);

adminSettings.Database = name;
var runtimeSettings = new NpgsqlConnectionStringBuilder(adminSettings.ConnectionString)
{
    Username = role,
    Password = password,
    Pooling = true,
    MaxPoolSize = 1,
    Multiplexing = false,
    NoResetOnClose = false,
};
try
{
    // Migration authority is separate from the runtime login. Runtime never executes DDL.
    using var setupTenancy = new TenantContextAccessor();
    setupTenancy.Initialize(TenantContext.ForTenant(new TenantId(Partitions.Alpha)));
    await using (
        var setup = new NotesContext(
            new DbContextOptionsBuilder<NotesContext>()
                .UseNpgsql(adminSettings.ConnectionString)
                .Options,
            setupTenancy
        )
    )
    {
        await setup.Database.MigrateAsync(token);
        Check(
            (await setup.Database.GetAppliedMigrationsAsync(token)).SequenceEqual(
                new[] { "202610240001_NotesAndPolicies" }
            ),
            "The consumer's versioned policy migration must be deployed."
        );
    }

    await using (var provision = new NpgsqlConnection(adminSettings.ConnectionString))
    {
        await provision.OpenAsync(token);
        await using var grant = new NpgsqlCommand(
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD '{password}'
                NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CONNECT ON DATABASE {name} TO "{role}";
            GRANT USAGE ON SCHEMA workspace TO "{role}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON workspace.notes TO "{role}";
            """,
            provision
        );
        await grant.ExecuteNonQueryAsync(token);
    }

    var services = new ServiceCollection();
    services.AddScoped<TenantContextAccessor>();
    services.AddScoped<ITenantContextAccessor>(provider =>
        provider.GetRequiredService<TenantContextAccessor>()
    );
    services.AddTenantOwnershipValidation<NotesContext, string>(database =>
        database.RequiredPartition
    );
    services.AddPostgresTenantRowSecurity<NotesContext>();
    services.AddDbContext<NotesContext>(
        (provider, options) =>
            options.UseNpgsql(runtimeSettings.ConnectionString).UseRls<NotesContext>(provider)
    );
    await using var provider = services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );

    foreach (
        (string partition, string body) in new[]
        {
            (Partitions.Alpha, "alpha-original"),
            (Partitions.Beta, "beta-original"),
        }
    )
    {
        await using var scope = Scope(partition);
        var database = scope.ServiceProvider.GetRequiredService<NotesContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        database.Notes.Add(new Note { Partition = partition, Body = body });
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    await using (var native = new NpgsqlConnection(runtimeSettings.ConnectionString))
    {
        await native.OpenAsync(token);
        await using var audit = new NpgsqlCommand(
            """
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole
                AND NOT r.rolcreatedb AND current_user = session_user
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                AND c.relrowsecurity AND c.relforcerowsecurity
                AND (SELECT count(*) FROM pg_policy WHERE polrelid = c.oid) = 1
                AND (SELECT polcmd = '*' AND polqual IS NOT NULL AND polwithcheck IS NOT NULL
                    FROM pg_policy WHERE polrelid = c.oid)
                AND NOT has_schema_privilege(current_user, 'workspace', 'CREATE')
            FROM pg_roles r CROSS JOIN pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE r.rolname = current_user AND n.nspname = 'workspace' AND c.relname = 'notes'
            """,
            native
        );
        Check(
            await audit.ExecuteScalarAsync(token) is true,
            "Actual runtime role, table and policy must have no bypass authority."
        );
        await using var noSetting = new NpgsqlCommand(
            "SELECT count(*) FROM workspace.notes",
            native
        );
        Check(
            (long)(await noSetting.ExecuteScalarAsync(token))! == 0,
            "Native reads without a tenant setting must deny all rows."
        );
        await using var blockedInsert = new NpgsqlCommand(
            "INSERT INTO workspace.notes VALUES (gen_random_uuid(), 'foreign', 'denied')",
            native
        );
        await Denied(() => blockedInsert.ExecuteNonQueryAsync(token));
    }

    int? backend = null;
    foreach (bool synchronous in new[] { true, false })
    {
        string partition = synchronous ? Partitions.Alpha : Partitions.Beta;
        string other = synchronous ? Partitions.Beta : Partitions.Alpha;
        await using var scope = Scope(partition);
        var database = scope.ServiceProvider.GetRequiredService<NotesContext>();
        await database.Database.OpenConnectionAsync(token);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        backend ??= connection.ProcessID;
        Check(
            connection.ProcessID == backend,
            "Both operations must reuse the same bounded native pool connection."
        );
        await using var transaction = synchronous
            ? database.Database.BeginTransaction()
            : await database.Database.BeginTransactionAsync(token);
        var existing = synchronous
            ? database.Notes.Single()
            : await database.Notes.SingleAsync(token);
        Check(
            existing.Partition == partition,
            "Scoped ordinary queries must select the admitted tenant."
        );
        Check(
            (await database.Notes.IgnoreQueryFilters().SingleAsync(token)).Partition == partition,
            "PostgreSQL must protect reads with EF filters disabled."
        );
        Check(
            await database
                .Notes.IgnoreQueryFilters()
                .Where(row => row.Partition == other)
                .CountAsync(token) == 0,
            "Foreign rows must remain invisible."
        );
        existing.Body = synchronous ? "alpha-committed" : "beta-rolled-back";
        database.Notes.Add(new Note { Partition = partition, Body = "new-owned-row" });
        Check(
            (synchronous ? database.SaveChanges() : await database.SaveChangesAsync(token)) == 2,
            "Native saving must update and insert the two owned rows."
        );
        Check(
            await database.Notes.IgnoreQueryFilters().CountAsync(token) == 2,
            "Both local writes must be visible in their transaction."
        );
        await using (
            var foreignUpdate = new NpgsqlCommand(
                "UPDATE workspace.notes SET body = 'foreign-write' WHERE partition = @other",
                connection,
                (NpgsqlTransaction)transaction.GetDbTransaction()
            )
        )
        {
            foreignUpdate.Parameters.AddWithValue("other", other);
            Check(
                await foreignUpdate.ExecuteNonQueryAsync(token) == 0,
                "RLS must protect native commands on the initialized caller transaction."
            );
        }

        Check(
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workspace.notes WHERE partition = {other}",
                token
            ) == 0,
            "RLS must also protect foreign deletes."
        );
        await transaction.CreateSavepointAsync("before_foreign", token);
        await Denied(() =>
            database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE workspace.notes SET partition = {other}",
                token
            )
        );
        await transaction.RollbackToSavepointAsync("before_foreign", token);
        await transaction.CreateSavepointAsync("before_insert", token);
        await Denied(() =>
            database.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO workspace.notes VALUES ({Guid.NewGuid()}, {other}, 'foreign-insert')",
                token
            )
        );
        await transaction.RollbackToSavepointAsync("before_insert", token);
        Check(
            await database.Notes.IgnoreQueryFilters().CountAsync(token) == 2,
            "Savepoint rollback must retain established tenancy."
        );
        if (synchronous)
            transaction.Commit();
        else
            await transaction.RollbackAsync(token);

        await using var baseline = new NpgsqlCommand(
            "SELECT NULLIF(current_setting('consumer.partition_key', true), '') IS NULL",
            connection
        );
        Check(
            await baseline.ExecuteScalarAsync(token) is true,
            "Commit and rollback must clear the local setting even while the connection remains open."
        );
    }

    foreach (
        (string partition, int count, string body) in new[]
        {
            (Partitions.Alpha, 2, "alpha-committed"),
            (Partitions.Beta, 1, "beta-original"),
        }
    )
    {
        await using var scope = Scope(partition);
        var database = scope.ServiceProvider.GetRequiredService<NotesContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var rows = await database.Notes.IgnoreQueryFilters().ToArrayAsync(token);
        Check(
            rows.Length == count
                && rows.All(row => row.Partition == partition)
                && rows.Any(row => row.Body == body),
            "Fresh operations must observe caller commit/rollback without cross-tenant leakage."
        );
        await transaction.CommitAsync(token);
    }

    await using (var scope = Scope(Partitions.Alpha))
    {
        var database = scope.ServiceProvider.GetRequiredService<NotesContext>();
        await Reject<InvalidOperationException>(
            () => database.Notes.IgnoreQueryFilters().CountAsync(token),
            "transaction"
        );
        Check(
            database.Database.CurrentTransaction is null,
            "Rejected reads must not invent a caller transaction."
        );
    }

    await using (var scope = provider.CreateAsyncScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<NotesContext>();
        await Reject<InvalidOperationException>(
            () => database.Database.BeginTransactionAsync(token),
            null
        );
        Check(
            database.Database.CurrentTransaction is null,
            "Missing tenancy must not publish a native transaction."
        );
    }

    await using (var scope = Scope(Partitions.Alpha))
    {
        var database = scope.ServiceProvider.GetRequiredService<NotesContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        database.Notes.Add(new Note { Partition = Partitions.Beta, Body = "foreign-tracked-row" });
        await Reject<TenantOwnershipException>(() => database.SaveChangesAsync(token), null);
        await transaction.RollbackAsync(token);
    }

    Console.WriteLine(
        "PASS RLS package adoption: versioned policies, restricted login, sync/async caller transactions, bypass SQL rejection, commit/rollback and pooled-connection isolation"
    );

    AsyncServiceScope Scope(string partition)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContextAccessor>()
            .Initialize(TenantContext.ForTenant(new TenantId(partition)));
        return scope;
    }
}
finally
{
    using var pool = new NpgsqlConnection(runtimeSettings.ConnectionString);
    NpgsqlConnection.ClearPool(pool);
    await using (var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin))
        await drop.ExecuteNonQueryAsync(CancellationToken.None);

    await using var dropRole = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", admin);
    await dropRole.ExecuteNonQueryAsync(CancellationToken.None);
}

static async Task Denied(Func<Task<int>> action)
{
    try
    {
        await action();
    }
    catch (PostgresException error)
        when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege)
    {
        return;
    }

    throw new InvalidOperationException(
        "The deployed PostgreSQL policy must reject a foreign write with SQLSTATE 42501."
    );
}

static async Task Reject<TException>(Func<Task> action, string? message)
    where TException : InvalidOperationException
{
    try
    {
        await action();
    }
    catch (TException error)
        when (message is null || error.Message.Contains(message, StringComparison.OrdinalIgnoreCase)
        )
    {
        return;
    }

    throw new InvalidOperationException("The selected setup must reject the invalid operation.");
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static class Partitions
{
    // Opaque values deliberately include characters requiring parameterized transaction setup.
    internal const string Alpha = "partition:α/'\n$selected";
    internal const string Beta = "partition:beta";
}

sealed class NotesContext(DbContextOptions<NotesContext> options, ITenantContextAccessor tenancy)
    : DbContext(options)
{
    internal string RequiredPartition => tenancy.Current.RequireTenant().Value;
    internal DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notes = modelBuilder.Entity<Note>();
        notes.ToTable("notes", "workspace");
        notes.HasKey(row => row.Id);
        notes.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        notes.Property(row => row.Partition).HasColumnName("partition");
        notes.Property(row => row.Body).HasColumnName("body");
        notes
            .HasTenantOwnership(row => row.Partition, () => RequiredPartition, "PartitionScope")
            .WithRls("consumer.partition_key");
    }
}

sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Partition { get; set; } = "";
    public string Body { get; set; } = "";
}

[DbContext(typeof(NotesContext))]
[Migration("202610240001_NotesAndPolicies")]
sealed class NotesAndPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE SCHEMA workspace;
            CREATE TABLE workspace.notes (id uuid PRIMARY KEY, partition text NOT NULL, body text NOT NULL);
            """
        );
        migrationBuilder.EnableTenantRowSecurity(
            "notes",
            "partition",
            "consumer.partition_key",
            "workspace"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantRowSecurity("notes", "workspace");
        migrationBuilder.Sql("DROP TABLE workspace.notes; DROP SCHEMA workspace;");
    }
}
