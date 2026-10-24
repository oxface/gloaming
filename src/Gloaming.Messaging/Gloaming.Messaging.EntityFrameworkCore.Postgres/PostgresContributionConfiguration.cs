using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Explicit fixed contribution mapping and native save integration; no clock, worker, migration or transaction is installed.</summary>
public static class PostgresContributionConfiguration
{
    internal const string Capability = "contributions.postgres.v1";

    /// <summary>Maps independent retained generation/contribution tables. ConfigurePostgresDueIntents and ConfigurePostgresOutbox are also required.</summary>
    public static void ConfigurePostgresContributions(
        this ModelBuilder model,
        string schema,
        string setsTable = "contribution_sets",
        string contributionsTable = "contributions"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        PostgresDueIntentConfiguration.ValidateIdentifier(schema);
        PostgresDueIntentConfiguration.ValidateIdentifier(setsTable);
        PostgresDueIntentConfiguration.ValidateIdentifier(contributionsTable);
        if (setsTable == contributionsTable)
            throw new ArgumentException("Use independent contribution-set and content tables.");

        model.RequireGloamingCapability(Capability);
        var set = model.Entity<ContributionSetRecord>();
        set.HasAnnotation(Capability, true);
        set.ToTable(
            setsTable,
            schema,
            mapping =>
            {
                mapping.HasCheckConstraint("ck_contribution_set_state", "state IN (1,2,3)");
                mapping.HasCheckConstraint("ck_contribution_set_revision", "revision > 0");
                mapping.HasCheckConstraint(
                    "ck_contribution_set_timeout",
                    "(due_at IS NULL AND timeout_signal IS NULL AND timeout_outcome IS NULL) OR (due_at IS NOT NULL AND timeout_signal IS NOT NULL AND timeout_outcome IS NOT NULL)"
                );
            }
        );
        set.HasKey(value => new
        {
            value.ScopeKey,
            value.WorkId,
            value.GenerationId,
        });
        set.Property(value => value.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(256)
            .UseCollation("C");
        set.Property(value => value.WorkId).HasColumnName("work_id").ValueGeneratedNever();
        set.Property(value => value.GenerationId)
            .HasColumnName("generation_id")
            .ValueGeneratedNever();
        set.Property(value => value.Expected).HasColumnName("expected").HasColumnType("jsonb");
        set.Property(value => value.Completion).HasColumnName("completion").HasColumnType("jsonb");
        set.Property(value => value.DueAt)
            .HasColumnName("due_at")
            .HasColumnType("timestamp with time zone")
            .HasPrecision(6);
        set.Property(value => value.TimeoutSignal)
            .HasColumnName("timeout_signal")
            .HasColumnType("jsonb");
        set.Property(value => value.TimeoutOutcome)
            .HasColumnName("timeout_outcome")
            .HasColumnType("jsonb");
        set.Property(value => value.Revision)
            .HasColumnName("revision")
            .HasColumnType("bigint")
            .IsConcurrencyToken();
        set.Property(value => value.State).HasColumnName("state").HasColumnType("integer");

        var contribution = model.Entity<ContributionRecord>();
        contribution.HasAnnotation(Capability, true);
        contribution.ToTable(
            contributionsTable,
            schema,
            mapping => mapping.HasCheckConstraint("ck_contribution_schema", "schema_version > 0")
        );
        contribution.HasKey(value => new
        {
            value.ScopeKey,
            value.WorkId,
            value.GenerationId,
            value.ContributorId,
        });
        contribution
            .Property(value => value.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(256)
            .UseCollation("C");
        contribution.Property(value => value.WorkId).HasColumnName("work_id").ValueGeneratedNever();
        contribution
            .Property(value => value.GenerationId)
            .HasColumnName("generation_id")
            .ValueGeneratedNever();
        contribution
            .Property(value => value.ContributorId)
            .HasColumnName("contributor_id")
            .HasMaxLength(256)
            .UseCollation("C");
        contribution.Property(value => value.Name).HasColumnName("name");
        contribution.Property(value => value.SchemaVersion).HasColumnName("schema_version");
        contribution
            .Property(value => value.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb");
        contribution
            .HasOne<ContributionSetRecord>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.ScopeKey,
                value.WorkId,
                value.GenerationId,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>Registers typed operations and required contribution/finite/outbox guards on the selected scoped native context.</summary>
    public static IServiceCollection AddPostgresContributions<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPostgresDueIntents<TContext>();
        services.TryAddScoped<PostgresContributionSets<TContext>>();
        return services.TryAddGloamingCapability<TContext>(
            Capability,
            CapabilityPhase.Validate,
            (_, database) => database.ValidateContributionChanges(),
            (_, database, token) => database.ValidateContributionChangesAsync(token)
        );
    }

    /// <summary>Manual synchronous contribution/finite/outbox validation, including native stored evidence reads for unchanged proposals.</summary>
    public static void ValidateContributionChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.ValidateDueIntentChanges();
        ContributionScope.For(database).Validate(database);
    }

    /// <summary>Manual asynchronous contribution/finite/outbox validation with cancellable native stored evidence reads.</summary>
    public static async Task ValidateContributionChangesAsync(
        this DbContext database,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(database);

        await database.ValidateDueIntentChangesAsync(cancellationToken);
        await ContributionScope.For(database).ValidateAsync(database, cancellationToken);
    }
}
