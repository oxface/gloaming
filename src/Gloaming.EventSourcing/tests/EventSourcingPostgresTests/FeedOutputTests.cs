using Gloaming.EventSourcing.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Gloaming.EventSourcing.Postgres.Tests.FeedConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class FeedOutputTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly FeedLimits Limits = new(32, 1024 * 1024);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertDefaultsDoNotPermitChangingExplicitInsertedOrUpdatedValues(bool update)
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        if (update)
        {
            await using var initial = Services(connection);
            Assert.Equal(
                ProjectionStatus.Applied,
                (await Feed(initial).ProjectNextAsync(Amount(10), Limits, Token)).Status
            );
            await LedgerAsync(connection, 2, 5, Token);
        }

        await using (var altered = Services(connection, new AlterOutput("default")))
            Assert.Equal(
                ProjectionStatus.Failed,
                (await Feed(altered).ProjectNextAsync(Amount(15), Limits, Token)).Status
            );

        await using var normal = Services(connection);
        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(normal).ProjectNextAsync(Amount(15), Limits, Token)).Status
        );
        await using var scope = normal.CreateAsyncScope();
        var row = await scope
            .ServiceProvider.GetRequiredService<Outputs>()
            .Set<AmountRow>()
            .SingleAsync(Token);
        Assert.Equal(15, row.Amount);
        Assert.Equal(30, row.Computed);
        Assert.True(row.Id > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeByteaHasAnIndependentSnapshotAndStructuralStoredComparison(bool mutate)
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        var generation = new ProjectionGeneration<Outputs>(
            "bytes",
            "v1",
            "bytes-v1",
            (db, selected, _, _) =>
            {
                db.Add(new BinaryRow { Generation = selected, Bytes = [1, 2] });
                return Task.CompletedTask;
            }
        );
        await using (
            var services = Services(connection, new AlterOutput(mutate ? "bytes" : "control"))
        )
            Assert.Equal(
                mutate ? ProjectionStatus.Failed : ProjectionStatus.Applied,
                (await Feed(services).ProjectNextAsync(generation, Limits, Token)).Status
            );

        await using var fresh = Services(connection);
        Assert.Equal(
            mutate ? ProjectionStatus.Applied : ProjectionStatus.NoWork,
            (await Feed(fresh).ProjectNextAsync(generation, Limits, Token)).Status
        );
        await using var scope = fresh.CreateAsyncScope();
        Assert.Equal(
            [1, 2],
            (
                await scope
                    .ServiceProvider.GetRequiredService<Outputs>()
                    .Set<BinaryRow>()
                    .SingleAsync(Token)
            ).Bytes
        );
    }

    [Fact]
    public async Task DeletionEvidenceUsesTheSelectedKeyAfterSavePreparation()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var normal = Services(connection);
        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(normal).ProjectNextAsync(Amount(10), Limits, Token)).Status
        );
        await LedgerAsync(connection, 2, 5, Token);
        var deletion = new ProjectionGeneration<Outputs>(
            "amount",
            "v1",
            "amount-v1",
            async (db, _, _, ct) => db.Remove(await db.Set<AmountRow>().SingleAsync(ct))
        );
        await using (var altered = Services(connection, new AlterOutput("delete")))
            Assert.Equal(
                ProjectionStatus.Failed,
                (await Feed(altered).ProjectNextAsync(deletion, Limits, Token)).Status
            );

        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(normal).ProjectNextAsync(deletion, Limits, Token)).Status
        );
        await using var scope = normal.CreateAsyncScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<Outputs>()
                .Set<AmountRow>()
                .ToArrayAsync(Token)
        );
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await SchemaAsync(connection, Token);
        await InstallAsync(connection, Token);
        await SqlAsync(
            connection,
            """
            CREATE TABLE app.amounts(id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                generation text NOT NULL, amount integer NOT NULL DEFAULT 0,
                computed integer GENERATED ALWAYS AS (amount * 2) STORED);
            CREATE TABLE app.binary_rows(id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                generation text NOT NULL, bytes bytea NOT NULL);
            """,
            Token
        );
        return connection;
    }

    [Theory]
    [InlineData("array")]
    [InlineData("complex")]
    [InlineData("view")]
    public async Task UnsupportedOutputShapeCannotCommitProgress(string shape)
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var generation = new ProjectionGeneration<Outputs>(
            "unsupported",
            "v1",
            "scalar-only",
            (db, _, _, _) =>
            {
                if (shape == "complex")
                    db.Add(new ComplexRow { Point = new() { Value = 10 } });
                else if (shape == "view")
                    db.Add(new DualReadRow { Value = 10 });
                else
                    db.Add(new ArrayRow { Values = [10] });

                return Task.CompletedTask;
            }
        );
        var result = await Feed(services).ProjectNextAsync(generation, Limits, Token);
        Assert.Equal(ProjectionStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
        var scalar = new ProjectionGeneration<Outputs>(
            "unsupported",
            "v1",
            "scalar-only",
            (db, _, _, _) =>
            {
                db.Add(new AmountRow { Generation = "v1", Amount = 10 });
                return Task.CompletedTask;
            }
        );
        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(services).ProjectNextAsync(scalar, Limits, Token)).Status
        );
    }

    private static ProjectionGeneration<Outputs> Amount(int amount) =>
        new(
            "amount",
            "v1",
            "amount-v1",
            async (db, selected, _, ct) =>
            {
                var row = await db.Set<AmountRow>().SingleOrDefaultAsync(ct);
                if (row is null)
                {
                    row = new() { Generation = selected };
                    db.Add(row);
                }

                row.Amount = amount;
            }
        );

    private static ServiceProvider Services(
        string connection,
        params IInterceptor[] interceptors
    ) =>
        new ServiceCollection()
            .AddDbContext<Outputs>(options =>
                options.UseNpgsql(connection).AddInterceptors(interceptors)
            )
            .BuildServiceProvider();

    private static PostgresEventFeed<Outputs> Feed(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), Location);

    private sealed class Outputs(DbContextOptions<Outputs> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var amount = modelBuilder.Entity<AmountRow>();
            amount.ToTable("amounts", "app");
            amount.HasKey(x => x.Id);
            amount.Property(x => x.Id).HasColumnName("id");
            amount.Property(x => x.Generation).HasColumnName("generation");
            amount.Property(x => x.Amount).HasColumnName("amount").HasDefaultValue(0);
            amount
                .Property(x => x.Computed)
                .HasColumnName("computed")
                .HasComputedColumnSql("amount * 2", stored: true);
            var binary = modelBuilder.Entity<BinaryRow>();
            binary.ToTable("binary_rows", "app");
            binary.HasKey(x => x.Id);
            binary.Property(x => x.Id).HasColumnName("id");
            binary.Property(x => x.Generation).HasColumnName("generation");
            binary.Property(x => x.Bytes).HasColumnName("bytes");
            modelBuilder.Entity<ArrayRow>().ToTable("unsupported_arrays", "app");
            var complex = modelBuilder.Entity<ComplexRow>();
            complex.ToTable("unsupported_complex", "app");
            complex.ComplexProperty(x => x.Point).IsRequired();
            var dual = modelBuilder.Entity<DualReadRow>();
            dual.ToTable("unsupported_dual", "app");
            dual.ToView("different_read_source", "app");
        }
    }

    private sealed class AmountRow
    {
        public int Id { get; set; }
        public string Generation { get; set; } = null!;
        public int Amount { get; set; }
        public int Computed { get; set; }
    }

    private sealed class BinaryRow
    {
        public int Id { get; set; }
        public string Generation { get; set; } = null!;
        public byte[] Bytes { get; set; } = null!;
    }

    private sealed class AlterOutput(string mode) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var database = eventData.Context!;
            if (mode == "default")
                foreach (var entry in database.ChangeTracker.Entries<AmountRow>())
                    entry.Entity.Amount = 0;
            else if (mode == "bytes")
                foreach (var entry in database.ChangeTracker.Entries<BinaryRow>())
                    entry.Entity.Bytes[0] = 99;
            else if (mode == "delete")
            {
                var row = database.ChangeTracker.Entries<AmountRow>().Single();
                database.ChangeTracker.AcceptAllChanges();
                row.Entity.Id = int.MaxValue;
                return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ArrayRow
    {
        public int Id { get; set; }
        public int[] Values { get; set; } = null!;
    }

    private sealed class ComplexRow
    {
        public int Id { get; set; }
        public Point Point { get; set; } = null!;
    }

    private sealed class Point
    {
        public int Value { get; set; }
    }

    private sealed class DualReadRow
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }
}
