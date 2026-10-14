using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Rootbolt.Persistence.EntityFrameworkCore;
using IsolationLevel = System.Data.IsolationLevel;

namespace ModulithFoundry.Samples.Wholesale.Sales.ProfileFeed;

// Every write is explicit module SQL in the owning transaction. Tracked writes are rejected.
internal static class FeedStorage
{
    internal static void Configure(ModelBuilder model, Expression<Func<string>> organization)
    {
        model.HasSequence<long>("customer_profile_feed_positions", "sales");
        var state = model.Entity<FeedStateRow>();
        state.ToTable(
            "customer_profile_feed_state",
            table =>
            {
                table.HasCheckConstraint("feed_bounds", "floor >= 0 AND head >= floor");
                table.HasCheckConstraint(
                    "feed_generation",
                    "generation <> '00000000-0000-0000-0000-000000000000'::uuid"
                );
            }
        );
        state.HasKey(row => row.OrganizationKey);
        state.HasIndex(row => row.Generation).IsUnique();
        state
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        state.Property(row => row.Generation).HasColumnName("generation").ValueGeneratedNever();
        state.Property(row => row.Head).HasColumnName("head");
        state.Property(row => row.Floor).HasColumnName("floor");
        state.HasTenantOwnership(row => row.OrganizationKey, organization, "OrganizationScope");

        var entry = model.Entity<ProfileChangeRow>();
        entry.ToTable(
            "customer_profile_changes",
            table =>
            {
                table.HasCheckConstraint("feed_position", "position > 0");
                table.HasCheckConstraint("feed_profile_version", "profile_version > 0");
                table.HasCheckConstraint(
                    "feed_change_identity",
                    "change_id <> '00000000-0000-0000-0000-000000000000'::uuid"
                );
            }
        );
        entry.HasKey(row => new { row.OrganizationKey, row.Position });
        entry.HasIndex(row => new { row.OrganizationKey, row.ChangeId }).IsUnique();
        entry
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        entry.Property(row => row.Position).HasColumnName("position").ValueGeneratedNever();
        entry.Property(row => row.ChangeId).HasColumnName("change_id").ValueGeneratedNever();
        entry.Property(row => row.ProfileVersion).HasColumnName("profile_version");
        entry.Property(row => row.Payload).HasColumnName("payload_v1").HasColumnType("jsonb");
        entry
            .HasOne<FeedStateRow>()
            .WithMany()
            .HasForeignKey(row => row.OrganizationKey)
            .OnDelete(DeleteBehavior.Restrict);
        entry.HasTenantOwnership(row => row.OrganizationKey, organization, "OrganizationScope");
    }

    internal static void ValidateTrackedChanges(SalesDbContext database)
    {
        foreach (var entry in database.ChangeTracker.Entries())
        {
            if (
                entry.Entity is FeedStateRow or ProfileChangeRow
                && entry.State is not (EntityState.Unchanged or EntityState.Detached)
            )
                throw new InvalidOperationException(
                    "Sales feed writes require the explicit locked SQL protocol."
                );
        }
    }

    internal static void RequireIndependent(SalesDbContext database)
    {
        if (
            database.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
            || database.ChangeTracker.HasChanges()
        )
            throw new InvalidOperationException(
                "Feed reads/maintenance require a fresh context without a transaction or pending changes."
            );

        _ = database.RequiredOrganizationKey;
    }

    internal static void RequireWriterTransaction(SalesDbContext database)
    {
        if (
            database.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel
                != IsolationLevel.ReadCommitted
            || System.Transactions.Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
        )
            throw new InvalidOperationException(
                "Profile feed writes require the owning native ReadCommitted transaction."
            );
    }

    internal static void ValidateCursor(CustomerProfileFeedCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentOutOfRangeException.ThrowIfEqual(cursor.Generation, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(cursor.AfterPosition);
    }

    internal static async Task LockAsync(SalesDbContext database, CancellationToken token)
    {
        RequireWriterTransaction(database);
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = database.Database.GetCommandTimeout() ?? 30;
        command.CommandText =
            "SELECT organization_key FROM sales.customer_profile_feed_state WHERE organization_key = @organization FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "organization";
        parameter.Value = database.RequiredOrganizationKey;
        command.Parameters.Add(parameter);
        if (await command.ExecuteScalarAsync(token) is null)
            throw new InvalidOperationException(
                "Activate the Sales profile feed explicitly before editing profiles."
            );
    }

    // Called only after LockAsync; the native row lock remains held through final commit.
    internal static async Task AppendAsync(
        SalesDbContext database,
        CustomerProfileChangedV1 profile,
        CancellationToken token
    )
    {
        ValidateProfile(profile);
        string json = JsonSerializer.Serialize(profile);
        long position = await database
            .Database.SqlQueryRaw<long>(
                "SELECT nextval('sales.customer_profile_feed_positions') AS \"Value\""
            )
            .SingleAsync(token);
        string organization = database.RequiredOrganizationKey;
        Guid identity = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sales.customer_profile_changes (organization_key, position, change_id, profile_version, payload_v1)
            VALUES ({organization}, {position}, {identity}, {profile.Version}, CAST({json} AS jsonb))
            """,
            token
        );
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE sales.customer_profile_feed_state SET head = {position} WHERE organization_key = {organization}
            """,
            token
        );
    }

    internal static void ValidateProfile(CustomerProfileChangedV1 profile)
    {
        if (
            profile.CustomerId == Guid.Empty
            || profile.AddressId == Guid.Empty
            || profile.Version < 1
            || string.IsNullOrWhiteSpace(profile.Code)
            || profile.Code.Length > 128
            || string.IsNullOrWhiteSpace(profile.DisplayName)
            || profile.DisplayName.Length > 256
            || string.IsNullOrWhiteSpace(profile.AddressLine)
            || profile.AddressLine.Length > 512
        )
            throw new InvalidDataException("Invalid Sales profile feed V1 payload.");
    }

    internal static CustomerProfileFeedBounds Bounds(FeedStateRow state) =>
        new(new(state.Generation, state.Floor), new(state.Generation, state.Head));
}
