using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RelationalOwnershipConsumer;

public class ValueShapesContext(DbContextOptions options, string tenant) : DbContext(options)
{
    public string RequiredTenant => tenant;
    public DbSet<ValueShapesRow> Rows => Set<ValueShapesRow>();
    protected virtual bool ProtectWrites => true;
    protected virtual bool RegisterOwnershipFirst => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<ValueShapesRow>().ToTable("rows", "value_shapes");
        row.Property(x => x.Id).ValueGeneratedNever();
        if (ProtectWrites && RegisterOwnershipFirst)
            row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "Scope");

        row.ComplexProperty(x => x.Position);
        row.ComplexProperty(x => x.OptionalPosition).IsRequired(false);
        row.ComplexProperty(
            x => x.Data,
            data =>
                data.Property(x => x.Code)
                    .HasConversion(value => value.Value, value => new ReferenceCode(value))
        );
        row.ComplexProperty(
            x => x.OptionalData,
            optional =>
            {
                optional.IsRequired(false);
                optional.HasDiscriminator();
            }
        );
        row.Property(x => x.JsonArray).HasColumnType("jsonb");
        if (ProtectWrites && !RegisterOwnershipFirst)
            row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "Scope");
        else if (!ProtectWrites)
            row.HasQueryFilter("Scope", x => x.Tenant == RequiredTenant);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (ProtectWrites)
            this.ValidateTenantChanges(() => RequiredTenant);

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        if (ProtectWrites)
            this.ValidateTenantChanges(() => RequiredTenant);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class NativeValueShapesContext(DbContextOptions options, string tenant)
    : ValueShapesContext(options, tenant)
{
    protected override bool ProtectWrites => false;
}

public sealed class EarlyValueShapesContext(DbContextOptions options, string tenant)
    : ValueShapesContext(options, tenant)
{
    protected override bool RegisterOwnershipFirst => true;
}

public sealed class ValueShapesRow
{
    public Guid Id { get; set; }
    public string Tenant { get; set; } = "";
    public Coordinate Position { get; set; } = new(1, 2);
    public Coordinate? OptionalPosition { get; set; }
    public ArrayData Data { get; set; } = new();
    public OptionalValue? OptionalData { get; set; }
    public string[] RootArray { get; set; } = ["Original"];
    public List<string> RootList { get; set; } = ["Original"];
    public string[] JsonArray { get; set; } = ["Original"];
}

public readonly record struct Coordinate(double Latitude, double Longitude);

public readonly record struct ReferenceCode(string Value);

public sealed class ArrayData
{
    public int[] Numbers { get; set; } = [1];
    public List<string> Names { get; set; } = ["Original"];
    public ReferenceCode Code { get; set; } = new("Original");
}

public sealed class OptionalValue
{
    public string? Text { get; set; }
}

// A native model control plus provider journey: no test framework or copied guard implementation.
public static class ValueShapeJourney
{
    public static async Task RunAsync(
        string connection,
        bool ownershipFirst = false,
        CancellationToken token = default
    )
    {
        var options = new DbContextOptionsBuilder().UseNpgsql(connection).Options;
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        await using (var native = new NativeValueShapesContext(options, "alpha"))
        await using (var guarded = Open("alpha"))
        {
            Require(
                native.Database.GenerateCreateScript() == guarded.Database.GenerateCreateScript(),
                "Ownership changed native value storage."
            );
            await native.Database.EnsureCreatedAsync(token);
            native.AddRange(
                new ValueShapesRow { Id = alphaId, Tenant = "alpha" },
                new ValueShapesRow { Id = betaId, Tenant = "beta" }
            );
            await native.SaveChangesAsync(token);
        }

        foreach (var asynchronous in new[] { false, true })
        {
            var label = asynchronous ? "async" : "sync";
            await using (var context = Open("alpha"))
            {
                var row = await context.Rows.SingleAsync(token);
                row.Position = new(3, 4);
                row.OptionalPosition = new(5, 6);
                row.OptionalData = new() { Text = null };
                row.RootArray[0] = label;
                row.RootList.Add(label);
                row.Data.Numbers[0]++;
                row.Data.Names[0] = label;
                row.Data.Code = new(label);
                row.JsonArray[0] = label;
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Rows.SingleAsync(token);
                Require(
                    row.Id == alphaId && row.Position == new Coordinate(3, 4),
                    "Required record struct did not round trip."
                );
                Require(
                    row.OptionalPosition == new Coordinate(5, 6),
                    "Nullable record struct did not round trip."
                );
                Require(
                    row.OptionalData is { Text: null },
                    "Optional all-null value lost its discriminator."
                );
                Require(
                    row.RootArray[0] == label && row.RootList[^1] == label,
                    "Root primitive collections did not round trip."
                );
                Require(
                    row.Data.Names[0] == label && row.Data.Numbers[0] == (asynchronous ? 4 : 2),
                    "Inline primitive collections did not round trip."
                );
                Require(
                    row.Data.Code == new ReferenceCode(label) && row.JsonArray[0] == label,
                    "Converted leaf or root JSON primitive collection did not round trip."
                );
                row.OptionalPosition = null;
                row.OptionalData = null;
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Rows.SingleAsync(token);
                Require(
                    row.OptionalPosition is null && row.OptionalData is null,
                    "Nullable complex removal was lost."
                );
                row.Data.Numbers[0]++;
                await Save(context, asynchronous);
            }

            await using (var afterCollectionEdit = Open("alpha"))
                Require(
                    (await afterCollectionEdit.Rows.SingleAsync(token)).Data.Numbers[0]
                        == (asynchronous ? 5 : 3),
                    "Inline primitive-collection-only update was lost."
                );

            await using (var context = Open("alpha"))
            {
                var foreign = await context
                    .Rows.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == betaId, token);
                foreign.Data.Numbers[0] = 100;
                var failure = await Rejected<TenantOwnershipException>(() =>
                    Save(context, asynchronous)
                );
                Require(
                    failure.Reason == TenantOwnershipFailure.ForeignOwner,
                    "Primitive-collection-only change bypassed ownership."
                );
            }

            await using (var context = Open("alpha"))
            {
                var forged = new ValueShapesRow { Id = betaId, Tenant = "alpha" };
                context.Attach(forged);
                forged.RootArray[0] = "forged";
                await Rejected<DbUpdateConcurrencyException>(() => Save(context, asynchronous));
            }

            await using var fresh = Open("beta");
            var saved = await fresh.Rows.SingleAsync(token);
            Require(
                saved.RootArray[0] == "Original" && saved.Data.Numbers[0] == 1,
                "Foreign primitive collection was changed."
            );
        }

        ValueShapesContext Open(string tenant)
        {
            ValueShapesContext context = ownershipFirst
                ? new EarlyValueShapesContext(options, tenant)
                : new ValueShapesContext(options, tenant);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            return context;
        }

        Task<int> Save(ValueShapesContext context, bool asynchronous) =>
            asynchronous ? context.SaveChangesAsync(token) : Task.FromResult(context.SaveChanges());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static async Task<T> Rejected<T>(Func<Task<int>> operation)
        where T : Exception
    {
        try
        {
            await operation();
        }
        catch (T failure)
        {
            return failure;
        }

        throw new InvalidOperationException(
            "The invalid value-shape operation unexpectedly succeeded."
        );
    }
}
