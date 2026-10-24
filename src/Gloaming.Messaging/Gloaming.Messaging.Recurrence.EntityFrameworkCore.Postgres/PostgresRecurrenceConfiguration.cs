using System.Text;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

/// <summary>Explicit recurrence mapping and save integration. No clock, transaction, migration or worker is installed.</summary>
public static class PostgresRecurrenceConfiguration
{
    internal const string Capability = "recurrence.postgres.v1";

    /// <summary>Maps retained immutable generations. ConfigurePostgresDueIntents and ConfigurePostgresOutbox are also required.</summary>
    public static EntityTypeBuilder<RecurringIntentRecord> ConfigurePostgresRecurrence(
        this ModelBuilder model,
        string schema,
        string table = "recurring_intents"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        Identifier(schema);
        Identifier(table);

        model.RequireGloamingCapability(Capability);
        var row = model.Entity<RecurringIntentRecord>();
        row.HasAnnotation(Capability, true);
        row.ToTable(
            table,
            schema,
            mapping =>
            {
                mapping.HasCheckConstraint("ck_recurrence_state", "state IN (1, 2, 3)");
                mapping.HasCheckConstraint("ck_recurrence_revision", "revision > 0");
            }
        );
        row.HasKey(value => new
        {
            value.ScopeKey,
            value.ScheduleId,
            value.GenerationId,
        });
        row.Property(value => value.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(256)
            .UseCollation("C");
        row.Property(value => value.ScheduleId).HasColumnName("schedule_id").ValueGeneratedNever();
        row.Property(value => value.GenerationId)
            .HasColumnName("generation_id")
            .ValueGeneratedNever();
        row.Property(value => value.Definition).HasColumnName("definition").HasColumnType("text");
        row.Property(value => value.Progress).HasColumnName("progress").HasColumnType("text");
        row.Property(value => value.Revision)
            .HasColumnName("revision")
            .HasColumnType("bigint")
            .IsConcurrencyToken();
        row.Property(value => value.State).HasColumnName("state").HasColumnType("integer");
        return row;
    }

    /// <summary>Registers typed recurrence operations and all required recurrence, finite-intent and outbox save guards.</summary>
    public static IServiceCollection AddPostgresRecurrence<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPostgresDueIntents<TContext>();
        services.TryAddScoped<PostgresRecurringIntents<TContext>>();
        return services.TryAddGloamingCapability<TContext>(
            Capability,
            CapabilityPhase.Validate,
            (_, database) => database.ValidateRecurrenceChanges(),
            (_, database, token) => database.ValidateRecurrenceChangesAsync(token)
        );
    }

    /// <summary>Manual synchronous integration including finite-intent and outbox guards and native reads for unchanged proposals.</summary>
    public static void ValidateRecurrenceChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.ValidateDueIntentChanges();
        RecurrenceScope.For(database).Validate(database);
    }

    /// <summary>Manual asynchronous integration including cancellable native reads for all unchanged proposals.</summary>
    public static async Task ValidateRecurrenceChangesAsync(
        this DbContext database,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(database);

        await database.ValidateDueIntentChangesAsync(cancellationToken);
        await RecurrenceScope.For(database).ValidateAsync(database, cancellationToken);
    }

    internal static void Identifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Contains('\0', StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(name) > 63)
            throw new ArgumentException(
                "Use a PostgreSQL identifier of at most 63 UTF-8 bytes.",
                nameof(name)
            );
    }
}
