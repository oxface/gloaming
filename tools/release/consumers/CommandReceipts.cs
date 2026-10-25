using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("A disposable PostgreSQL server is required.");

string name = "gloaming_receipts_" + Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync();

try
{
    settings.Database = name;
    var options = new DbContextOptionsBuilder<ReceiptDatabase>()
        .UseNpgsql(settings.ConnectionString)
        .Options;
    await using (var setup = new ReceiptDatabase(options))
        await setup.Database.EnsureCreatedAsync();

    var key = new CommandReceiptKey("admitted", "publish", Guid.NewGuid());
    for (int attempt = 0; attempt < 2; attempt++)
    {
        await using var database = new ReceiptDatabase(options);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var receipt = await new PostgresCommandReceipts<ReceiptDatabase>(database).AcquireAsync(
            key,
            "stable-input"
        );
        if (receipt.Result is { } retained)
        {
            if (attempt != 1 || retained != new CommandReceiptContent(1, "published"))
                throw new InvalidOperationException(
                    "The package did not replay its original result."
                );
        }
        else
        {
            if (attempt != 0)
                throw new InvalidOperationException("The receipt disappeared.");

            database.Add(new Effect { Id = key.OperationId });
            receipt.Retain(new(1, "published"));
            await database.SaveChangesAsync(acceptAllChangesOnSuccess: false);
            database.ChangeTracker.AcceptAllChanges();
            if (database.SaveChanges() != 0 || await database.SaveChangesAsync() != 0)
                throw new InvalidOperationException("A later save repeated the retained proposal.");

            await transaction.CommitAsync();
        }
    }

    foreach (bool asynchronous in new[] { false, true })
    {
        await using var database = new ReceiptDatabase(options);
        await using var transaction = await database.Database.BeginTransactionAsync();
        var pendingKey = new CommandReceiptKey("admitted", "suppressed", Guid.NewGuid());
        var receipt = await new PostgresCommandReceipts<ReceiptDatabase>(database).AcquireAsync(
            pendingKey,
            "stable-input"
        );
        database.Add(new Effect { Id = pendingKey.OperationId });
        receipt.Retain(new(1, "must-not-commit"));
        database.ChangeTracker.Entries<CommandReceiptRecord>().Single().State =
            EntityState.Unchanged;
        bool rejected = false;
        try
        {
            if (asynchronous)
                await database.SaveChangesAsync();
            else
                database.SaveChanges();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        if (!rejected)
            throw new InvalidOperationException("A native save accepted an unpersisted receipt.");

        await transaction.RollbackAsync();
    }

    await using var read = new ReceiptDatabase(options);
    if (
        await read.Set<Effect>().CountAsync() != 1
        || await read.Set<CommandReceiptRecord>().CountAsync() != 1
    )
        throw new InvalidOperationException("The package repeated a retained business effect.");

    Console.WriteLine("Command receipt package adoption passed.");
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
}

sealed class ReceiptDatabase(DbContextOptions<ReceiptDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureCommandReceipts("adopter", "receipts");
        model.Entity<Effect>().ToTable("effects", "adopter").HasKey(value => value.Id);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateCommandReceiptChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateCommandReceiptChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

sealed class Effect
{
    public Guid Id { get; set; }
}
