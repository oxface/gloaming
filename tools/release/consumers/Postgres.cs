using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("GLOAMING_PACKAGE_TEST_CONNECTION_STRING is required.");
string databaseName = "gloaming_package_" + Guid.NewGuid().ToString("N");
var adminOptions = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(adminOptions.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
    await create.ExecuteNonQueryAsync();

try
{
    var options = new NpgsqlConnectionStringBuilder(supplied)
    {
        Database = databaseName,
        Pooling = false,
    };
    var services = new ServiceCollection();
    services.AddTenantOwnershipValidation<PackageDatabase, string>(context =>
        context.RequiredTenant
    );
    services.AddOutboxValidation<PackageDatabase>();
    services.AddAuditValidation<PackageDatabase>();
    using var provider = services.BuildServiceProvider();
    PackageDatabase Open(string tenant = "tenant-a")
    {
        var builder = new DbContextOptionsBuilder<PackageDatabase>().UseNpgsql(
            options.ConnectionString
        );
        builder.ConfigureGloamingCapabilities<PackageDatabase>(provider);
        return new PackageDatabase(builder.Options, tenant);
    }

    await using (var setup = Open())
        await setup.Database.EnsureCreatedAsync();

    Guid committed = Guid.NewGuid();
    await using (var writer = Open())
    {
        await using var transaction = await writer.Database.BeginTransactionAsync();
        Stage(writer, committed);
        await using (var beforeSave = Open())
            await Counts(beforeSave, 0, 0, 0);

        await writer.SaveChangesAsync();
        await using (var beforeCommit = Open())
            await Counts(beforeCommit, 0, 0, 0);

        await transaction.CommitAsync();
    }

    await using (var read = Open())
    {
        await Counts(read, 1, 1, 1);
        Check(
            (await read.Set<Document>().SingleAsync()).Id == committed,
            "Business identity persisted."
        );
        var envelope = await read.Set<OutboxMessageRecord>().SingleAsync();
        Check(
            envelope.TenantKey == "tenant-a"
                && envelope.Payload.GetProperty("amount").GetInt32() == 7,
            "Outbox metadata and JSON retained."
        );
        var audit = await read.Set<AuditRecord>().SingleAsync();
        Check(
            audit.ActorKind == ActorKind.System && audit.ActorKey == "package-proof",
            "Audit attribution retained."
        );
    }

    await using (var rollback = Open())
    {
        await using var transaction = await rollback.Database.BeginTransactionAsync();
        Stage(rollback, Guid.NewGuid());
        rollback.SaveChanges();
        await transaction.RollbackAsync();
    }

    await using (var failed = Open())
    {
        await using var transaction = await failed.Database.BeginTransactionAsync();
        failed.Add(
            new Document
            {
                Id = Guid.NewGuid(),
                Tenant = "tenant-a",
                Amount = 3,
            }
        );
        await failed.SaveChangesAsync();
        Stage(failed, Guid.NewGuid(), amount: -1);
        try
        {
            await failed.SaveChangesAsync();
            throw new InvalidOperationException("Expected native check constraint failure.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.CheckViolation }
            ) { }

        await transaction.RollbackAsync();
    }

    await using (var recovered = Open())
    {
        await Counts(recovered, 1, 1, 1);
        await using var transaction = await recovered.Database.BeginTransactionAsync();
        Stage(recovered, Guid.NewGuid());
        await recovered.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    await using (var foreign = Open("tenant-b"))
    {
        Check(
            !await foreign.Set<Document>().AnyAsync(),
            "Named ownership filter excludes other tenants."
        );
        foreign.Add(
            new Document
            {
                Id = Guid.NewGuid(),
                Tenant = "tenant-a",
                Amount = 4,
            }
        );
        try
        {
            await foreign.SaveChangesAsync();
            throw new InvalidOperationException("Expected foreign-owner rejection.");
        }
        catch (TenantOwnershipException exception)
            when (exception.Reason == TenantOwnershipFailure.ForeignOwner) { }
    }

    await using (var fresh = Open())
        await Counts(fresh, 2, 2, 2);

    Console.WriteLine(
        "PASS PostgreSQL: package-only ownership, outbox and audit; native commit, rollback, fault and fresh-context recovery"
    );
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
}

static void Stage(PackageDatabase database, Guid id, int amount = 7)
{
    database.Add(
        new Document
        {
            Id = id,
            Tenant = "tenant-a",
            Amount = amount,
        }
    );
    new EfOutbox<PackageDatabase>(database).Enqueue(
        new OutgoingMessage(
            Guid.NewGuid(),
            "exports",
            "document.changed",
            1,
            JsonSerializer.SerializeToElement(new { amount }),
            "tenant-a"
        )
    );
    new EfAudit<PackageDatabase>(database).Stage(
        new AuditEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new ActorContext(Actor.System(new ActorId("package-proof"))),
            "documents",
            "document.changed",
            "document",
            id.ToString(),
            "accepted",
            1,
            JsonSerializer.SerializeToElement(new { amount }),
            tenantKey: "tenant-a"
        )
    );
}

static async Task Counts(PackageDatabase database, int business, int outgoing, int audit)
{
    Check(await database.Set<Document>().CountAsync() == business, "Business count.");
    Check(await database.Set<OutboxMessageRecord>().CountAsync() == outgoing, "Outbox count.");
    Check(await database.Set<AuditRecord>().CountAsync() == audit, "Audit count.");
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class Document
{
    public Guid Id { get; set; }
    public string Tenant { get; set; } = "";
    public int Amount { get; set; }
}

sealed class PackageDatabase(DbContextOptions<PackageDatabase> options, string tenant)
    : DbContext(options)
{
    public string RequiredTenant => tenant;

    protected override void OnModelCreating(ModelBuilder model)
    {
        var documents = model.Entity<Document>();
        documents.ToTable(
            "documents",
            "package_proof",
            table => table.HasCheckConstraint("positive_amount", "\"Amount\" > 0")
        );
        documents.HasKey(row => row.Id);
        documents.Property(row => row.Id).ValueGeneratedNever();
        documents.HasTenantOwnership(row => row.Tenant, () => RequiredTenant, "Owner");
        model.ConfigurePostgresOutbox("package_proof", "outbox");
        model.ConfigureAudit("package_proof", "audit")
            .Property(row => row.Details)
            .HasColumnType("jsonb");
    }
}
