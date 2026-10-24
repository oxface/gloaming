using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.EventSourcing.Postgres.Tests;

public static class FeedConsumer
{
    public static readonly EventFeedLocation Location = new(
        "feed",
        Guid.Parse("00000000-0000-0000-0000-000000000081")
    );
    public static readonly Guid StreamId = Guid.Parse("00000000-0000-0000-0000-000000000082");
    public static readonly EventFeedSource[] Sources =
    [
        new("ledger", "app", "ledger", ["stream_id"], "version", ["id"]),
        new("reviews", "app", "reviews", ["tenant", "review"], "version", ["tenant", "id"]),
    ];

    public static async Task SchemaAsync(string connection, CancellationToken token)
    {
        await SqlAsync(
            connection,
            """
            CREATE SCHEMA app; CREATE SCHEMA feed;
            CREATE TABLE app.ledger(id uuid PRIMARY KEY, stream_id uuid NOT NULL, version bigint NOT NULL,
                name text NOT NULL, schema integer NOT NULL, payload jsonb NOT NULL, UNIQUE(stream_id, version));
            CREATE TABLE app.reviews(tenant text COLLATE "C" NOT NULL, id uuid NOT NULL, review text COLLATE "C" NOT NULL,
                version bigint NOT NULL, name text NOT NULL, schema integer NOT NULL, payload jsonb NOT NULL,
                PRIMARY KEY(tenant, id), UNIQUE(tenant, review, version));
            CREATE TABLE app.ledger_view(generation text COLLATE "C" NOT NULL, id uuid NOT NULL, amount integer NOT NULL,
                PRIMARY KEY(generation, id));
            CREATE TABLE app.review_view(generation text COLLATE "C" NOT NULL, tenant text COLLATE "C" NOT NULL,
                review text COLLATE "C" NOT NULL, amount integer NOT NULL, PRIMARY KEY(generation, tenant, review));
            """,
            token
        );
    }

    public static async Task InstallAsync(string connection, CancellationToken token)
    {
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(token);
        await using var transaction = await session.BeginTransactionAsync(token);
        await using var command = new NpgsqlCommand(
            EventFeedInstallation.Create(Location, Sources),
            session,
            transaction
        );
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public static ServiceProvider Services(string connection, params IInterceptor[] interceptors) =>
        new ServiceCollection()
            .AddDbContext<Views>(options =>
                options.UseNpgsql(connection).AddInterceptors(interceptors)
            )
            .BuildServiceProvider();

    public static PostgresEventFeed<Views> Feed(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), Location);

    public static ProjectionGeneration<Views> Ledger(
        string generation = "v1",
        string definition = "ledger-v1"
    ) =>
        new(
            "ledger",
            generation,
            definition,
            async (database, selected, page, token) =>
            {
                foreach (var item in page.Events.Where(x => x.Source == "ledger"))
                {
                    Require(item, "ledger.adjusted");
                    Guid id = item.StreamKey[0].GetGuid();
                    var row =
                        database
                            .Set<LedgerView>()
                            .Local.SingleOrDefault(x => x.Generation == selected && x.Id == id)
                        ?? await database
                            .Set<LedgerView>()
                            .SingleOrDefaultAsync(
                                x => x.Generation == selected && x.Id == id,
                                token
                            );
                    if (row is null)
                    {
                        row = new() { Generation = selected, Id = id };
                        database.Add(row);
                    }

                    row.Amount += item
                        .Envelope.GetProperty("payload")
                        .GetProperty("amount")
                        .GetInt32();
                }
            }
        );

    public static ProjectionGeneration<Views> Reviews(string generation = "v1") =>
        new(
            "reviews",
            generation,
            "review-count-v1",
            async (database, selected, page, token) =>
            {
                foreach (var item in page.Events.Where(x => x.Source == "reviews"))
                {
                    Require(item, "review.contributed");
                    string tenant = item.StreamKey[0].GetString()!;
                    string review = item.StreamKey[1].GetString()!;
                    var row =
                        database
                            .Set<ReviewView>()
                            .Local.SingleOrDefault(x =>
                                x.Generation == selected && x.Tenant == tenant && x.Review == review
                            )
                        ?? await database
                            .Set<ReviewView>()
                            .SingleOrDefaultAsync(
                                x =>
                                    x.Generation == selected
                                    && x.Tenant == tenant
                                    && x.Review == review,
                                token
                            );
                    if (row is null)
                    {
                        row = new()
                        {
                            Generation = selected,
                            Tenant = tenant,
                            Review = review,
                        };
                        database.Add(row);
                    }

                    row.Amount++;
                }
            }
        );

    private static void Require(FeedEvent item, string name)
    {
        if (
            item.Envelope.GetProperty("name").GetString() != name
            || item.Envelope.GetProperty("schema").GetInt32() != 1
        )
            throw new InvalidOperationException(
                "Unknown retained alias/schema stops this projection."
            );
    }

    public static async Task SqlAsync(string connection, string sql, CancellationToken token)
    {
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, session);
        await command.ExecuteNonQueryAsync(token);
    }

    public static async Task LedgerAsync(
        NpgsqlConnection session,
        NpgsqlTransaction transaction,
        long version,
        int amount,
        CancellationToken token,
        string name = "ledger.adjusted"
    )
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO app.ledger VALUES(@id, @stream, @version, @name, 1, @payload::jsonb)",
            session,
            transaction
        );
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("stream", StreamId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(new { amount }));
        await command.ExecuteNonQueryAsync(token);
    }

    public static async Task LedgerAsync(
        string connection,
        long version,
        int amount,
        CancellationToken token,
        string name = "ledger.adjusted"
    )
    {
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(token);
        await using var transaction = await session.BeginTransactionAsync(token);
        await LedgerAsync(session, transaction, version, amount, token, name);
        await transaction.CommitAsync(token);
    }

    public sealed class Views(DbContextOptions<Views> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var ledger = modelBuilder.Entity<LedgerView>();
            ledger.ToTable("ledger_view", "app");
            ledger.HasKey(x => new { x.Generation, x.Id });
            ledger.Property(x => x.Generation).HasColumnName("generation");
            ledger.Property(x => x.Id).HasColumnName("id");
            ledger.Property(x => x.Amount).HasColumnName("amount");
            var reviews = modelBuilder.Entity<ReviewView>();
            reviews.ToTable("review_view", "app");
            reviews.HasKey(x => new
            {
                x.Generation,
                x.Tenant,
                x.Review,
            });
            reviews.Property(x => x.Generation).HasColumnName("generation");
            reviews.Property(x => x.Tenant).HasColumnName("tenant");
            reviews.Property(x => x.Review).HasColumnName("review");
            reviews.Property(x => x.Amount).HasColumnName("amount");
        }
    }

    public sealed class LedgerView
    {
        public string Generation { get; set; } = null!;
        public Guid Id { get; set; }
        public int Amount { get; set; }
    }

    public sealed class ReviewView
    {
        public string Generation { get; set; } = null!;
        public string Tenant { get; set; } = null!;
        public string Review { get; set; } = null!;
        public int Amount { get; set; }
    }
}
