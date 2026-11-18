using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// Disposable application policy inspired by Umbrine's FND-04/FND-10. This is not its model.
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("GLOAMING_PACKAGE_TEST_CONNECTION_STRING is required.");
string name = "gloaming_personnel_" + Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync(token);

settings.Database = name;
var options = new DbContextOptionsBuilder<PersonnelDatabase>()
    .UseNpgsql(settings.ConnectionString)
    .Options;
var personId = Guid.NewGuid();
var morgan = new ActorContext(Actor.Human(new ActorId("morgan")));
var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
Exception? proofFailure = null;
try
{
    using (var tenancy = Selected("aster"))
    await using (var setup = new PersonnelDatabase(options, tenancy))
    {
        await setup.Database.MigrateAsync(token);
        Check(
            (await setup.Database.GetAppliedMigrationsAsync(token)).SequenceEqual(
                new[] { "202610240002_PersonnelAndHistory" }
            ),
            "The disposable consumer must deploy its own versioned schema."
        );
        // Fixture data, including deliberately equal person IDs across organizations.
        await setup.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO personnel.people VALUES
                ({personId}, 'aster', 'Riley A', 1), ({personId}, 'birch', 'Riley B', 1);
            INSERT INTO personnel.memberships VALUES
                ('aster', 'morgan', true, true), ('birch', 'morgan', true, true),
                ('aster', 'technical-admin', true, false);
            """,
            token
        );
    }

    Check(await Rename("aster", morgan, "Riley A1"), "Aster's admitted change must commit.");
    Check(await Rename("birch", morgan, "Riley B1"), "Birch's independent change must commit.");
    await Expected("aster", "Riley A1", 2, [("Riley A", "Riley A1")]);
    await Expected("birch", "Riley B1", 2, [("Riley B", "Riley B1")]);

    using (var tenancy = Selected("aster"))
    await using (var suspend = new PersonnelDatabase(options, tenancy))
        await suspend.Database.ExecuteSqlRawAsync(
            "UPDATE personnel.memberships SET active = false WHERE organization = 'aster' AND user_key = 'morgan'",
            token
        );

    Check(!await Rename("aster", morgan, "denied"), "A new suspended operation must be denied.");
    Check(
        !await Rename(
            "aster",
            new ActorContext(Actor.Human(new ActorId("technical-admin"))),
            "denied"
        ),
        "Technical administration must not supply the selected business permission."
    );
    Check(
        !await Rename("birch", new ActorContext(Actor.Anonymous), "denied"),
        "Selecting tenancy must not turn anonymity into an admitted actor."
    );
    Check(await Rename("birch", morgan, "Riley B2"), "Suspension must leave Birch access intact.");
    await Expected("aster", "Riley A1", 2, [("Riley A", "Riley A1")]);
    await Expected("birch", "Riley B2", 3, [("Riley B", "Riley B1"), ("Riley B1", "Riley B2")]);

    // Exercise both native save forms, with rollback after SQL and a real database failure.
    foreach (bool synchronous in new[] { true, false })
    {
        Check(
            await Rename("birch", morgan, "rolled-back", synchronous, rollback: true),
            "The admitted proposal must reach the explicit rollback."
        );
        try
        {
            await Rename("birch", morgan, "", synchronous);
            throw new InvalidOperationException("Expected the native business constraint to fail.");
        }
        catch (DbUpdateException error)
            when (error.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.CheckViolation }
            ) { }

        await Expected("birch", "Riley B2", 3, [("Riley B", "Riley B1"), ("Riley B1", "Riley B2")]);
    }

    // Tracked saves must reject a row explicitly assigned to another organization.
    using (var tenancy = Selected("birch"))
    await using (var foreign = new PersonnelDatabase(options, tenancy))
    {
        foreign.Add(
            new Person
            {
                Id = Guid.NewGuid(),
                Organization = "aster",
                Name = "denied",
                Revision = 1,
            }
        );
        try
        {
            await foreign.SaveChangesAsync(token);
            throw new InvalidOperationException("Expected tracked foreign-owner rejection.");
        }
        catch (TenantOwnershipException error)
            when (error.Reason == TenantOwnershipFailure.ForeignOwner) { }
    }

    Check(
        await Rename("birch", morgan, "Riley B3", synchronous: true),
        "Fresh recovery must commit once."
    );
    await Expected(
        "birch",
        "Riley B3",
        4,
        [("Riley B", "Riley B1"), ("Riley B1", "Riley B2"), ("Riley B2", "Riley B3")]
    );
    await Expected("aster", "Riley A1", 2, [("Riley A", "Riley A1")]);
    Console.WriteLine(
        "PASS Personnel slice: current admission, independent organizations, attributable history and atomic failure recovery"
    );
    // Cleanup must still complete when the operation's budget has already been cancelled.
    deadline.Cancel();
}
catch (Exception error)
{
    proofFailure = error;
    throw;
}
finally
{
    using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try
    {
        await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync(cleanupDeadline.Token);
    }
    catch (Exception error) when (proofFailure is not null)
    {
        Console.Error.WriteLine($"Personnel proof cleanup failed ({error.GetType().Name}).");
    }
}

async Task<bool> Rename(
    string organization,
    ActorContext actor,
    string replacement,
    bool synchronous = false,
    bool rollback = false
)
{
    if (actor.Actor.Kind != ActorKind.Human)
        return false;

    // Admission/permissions are this consumer's policy, queried afresh for every operation.
    await using var admission = new NpgsqlConnection(settings.ConnectionString);
    await admission.OpenAsync(token);
    await using var allowed = new NpgsqlCommand(
        "SELECT active AND can_edit FROM personnel.memberships WHERE organization = @organization AND user_key = @user",
        admission
    );
    allowed.Parameters.AddWithValue("organization", organization);
    allowed.Parameters.AddWithValue("user", actor.Actor.Id!.Value);
    if (await allowed.ExecuteScalarAsync(token) is not true)
        return false;

    using var tenancy = Selected(organization);
    await using var database = new PersonnelDatabase(options, tenancy);
    await using var transaction = await database.Database.BeginTransactionAsync(token);
    var person = await database.Set<Person>().SingleAsync(row => row.Id == personId, token);
    string before = person.Name;
    person.Name = replacement;
    person.Revision++;
    new EfAudit<PersonnelDatabase>(database).Stage(
        new AuditEntry(
            Guid.NewGuid(),
            now,
            actor,
            "personnel",
            "person.renamed",
            "person",
            personId.ToString(),
            "accepted",
            1,
            JsonSerializer.SerializeToElement(
                new
                {
                    Before = before,
                    After = replacement,
                    person.Revision,
                }
            ),
            tenantKey: organization
        )
    );
    if (synchronous)
        database.SaveChanges();
    else
        await database.SaveChangesAsync(token);

    // Save must not publish either participant before the caller's commit.
    using (var observerTenancy = Selected(organization))
    await using (var observer = new PersonnelDatabase(options, observerTenancy))
        Check(
            (await observer.Set<Person>().SingleAsync(token)).Name == before,
            "A separate connection must still see the previous committed business state."
        );

    if (rollback)
        await transaction.RollbackAsync(token);
    else
        await transaction.CommitAsync(token);

    return true;
}

async Task Expected(
    string organization,
    string expectedName,
    long revision,
    (string Before, string After)[] changes
)
{
    using var tenancy = Selected(organization);
    await using var database = new PersonnelDatabase(options, tenancy);
    var person = await database.Set<Person>().SingleAsync(token);
    Check(
        person.Id == personId
            && person.Organization == organization
            && person.Name == expectedName
            && person.Revision == revision,
        "Ordinary filtered reads must return exactly the expected organization's retained state."
    );
    var history = await database
        .Set<AuditRecord>()
        .OrderBy(row => row.Details.GetProperty("Revision").GetInt64())
        .ToArrayAsync(token);
    Check(
        history.Length == changes.Length,
        "Only committed changes may remain in this organization's history."
    );
    for (int index = 0; index < changes.Length; index++)
    {
        var entry = history[index];
        Check(
            entry.TenantKey == organization
                && entry.ActorKind == ActorKind.Human
                && entry.ActorKey == "morgan"
                && entry.InitiatorKind is null
                && entry.OccurredAt == now
                && entry.SubjectType == "person"
                && entry.SubjectKey == personId.ToString()
                && entry.Source == "personnel"
                && entry.Action == "person.renamed"
                && entry.Outcome == "accepted"
                && entry.SchemaVersion == 1
                && entry.Details.GetProperty("Before").GetString() == changes[index].Before
                && entry.Details.GetProperty("After").GetString() == changes[index].After
                && entry.Details.GetProperty("Revision").GetInt64() == index + 2,
            "History must preserve independently expected actor, time, source and each accepted before/after state."
        );
    }
}

static TenantContextAccessor Selected(string organization)
{
    var accessor = new TenantContextAccessor();
    accessor.Initialize(TenantContext.ForTenant(new TenantId(organization)));
    return accessor;
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class Person
{
    public Guid Id { get; set; }
    public string Organization { get; set; } = "";
    public string Name { get; set; } = "";
    public long Revision { get; set; }
}

sealed class PersonnelDatabase(
    DbContextOptions<PersonnelDatabase> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    internal string RequiredOrganization => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder model)
    {
        var people = model.Entity<Person>();
        people.ToTable(
            "people",
            "personnel",
            table => table.HasCheckConstraint("person_name_required", "length(name) > 0")
        );
        people.HasKey(row => new { row.Organization, row.Id });
        people.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        people.Property(row => row.Organization).HasColumnName("organization");
        people.Property(row => row.Name).HasColumnName("name");
        people.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        people.HasTenantOwnership(
            row => row.Organization,
            () => RequiredOrganization,
            "OrganizationScope"
        );
        var history = model.ConfigureAudit("personnel", "history");
        history.Property(row => row.Details).HasColumnType("jsonb");
        history.HasQueryFilter(row => row.TenantKey == RequiredOrganization);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredOrganization);
        this.ValidateAuditChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateTenantChanges(() => RequiredOrganization);
        this.ValidateAuditChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

[DbContext(typeof(PersonnelDatabase))]
[Migration("202610240002_PersonnelAndHistory")]
sealed class PersonnelAndHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE SCHEMA personnel;
            CREATE TABLE personnel.memberships (
                organization text NOT NULL, user_key text NOT NULL, active boolean NOT NULL,
                can_edit boolean NOT NULL, PRIMARY KEY (organization, user_key));
            CREATE TABLE personnel.people (
                id uuid NOT NULL, organization text NOT NULL, name text NOT NULL, revision bigint NOT NULL,
                PRIMARY KEY (organization, id), CONSTRAINT person_name_required CHECK (length(name) > 0));
            CREATE TABLE personnel.history (
                id uuid PRIMARY KEY, occurred_at timestamptz NOT NULL, actor_kind integer NOT NULL,
                actor_key text, initiator_kind integer, initiator_key text, source text NOT NULL,
                action text NOT NULL, subject_type text NOT NULL, subject_key text, outcome text NOT NULL,
                schema_version integer NOT NULL, details jsonb NOT NULL, reason_code text, tenant_key text);
            CREATE INDEX ix_audit_subject_timeline ON personnel.history
                (tenant_key, subject_type, subject_key, occurred_at, id);
            """
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP SCHEMA personnel CASCADE");
}
