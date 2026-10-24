using System.Text;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Explicit occurrence mapping and native save integration. No clock, worker or transaction is installed.</summary>
public static class PostgresDueIntentConfiguration
{
    internal const string Capability = "due-intents.postgres.v1";

    /// <summary>Maps the fixed provided model. ConfigurePostgresOutbox and consumer-owned migrations are also required.</summary>
    public static EntityTypeBuilder<DueIntentRecord> ConfigurePostgresDueIntents(
        this ModelBuilder model,
        string schema,
        string table = "due_intents"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateIdentifier(schema);
        ValidateIdentifier(table);

        model.RequireGloamingCapability(Capability);
        var row = model.Entity<DueIntentRecord>();
        row.HasAnnotation(Capability, true);
        row.ToTable(
            table,
            schema,
            mapping =>
            {
                mapping.HasCheckConstraint("ck_due_intent_state", "state IN (1, 2, 3)");
                mapping.HasCheckConstraint("ck_due_intent_schema", "schema_version > 0");
            }
        );
        row.HasKey(value => new
        {
            value.ScopeKey,
            value.ScheduleId,
            value.OccurrenceId,
        });
        row.Property(value => value.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(256)
            .UseCollation("C");
        row.Property(value => value.ScheduleId).HasColumnName("schedule_id").ValueGeneratedNever();
        row.Property(value => value.OccurrenceId)
            .HasColumnName("occurrence_id")
            .ValueGeneratedNever();
        row.Property(value => value.DueAt)
            .HasColumnName("due_at")
            .HasColumnType("timestamp with time zone")
            .HasPrecision(6);
        row.Property(value => value.State)
            .HasColumnName("state")
            .HasColumnType("integer")
            .IsConcurrencyToken();
        row.Property(value => value.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        row.Property(value => value.RouteKey).HasColumnName("destination");
        row.Property(value => value.MessageName).HasColumnName("message_name");
        row.Property(value => value.SchemaVersion).HasColumnName("schema_version");
        row.Property(value => value.Payload).HasColumnName("payload").HasColumnType("jsonb");
        row.Property(value => value.TenantKey).HasColumnName("tenant_key");
        row.Property(value => value.CorrelationId).HasColumnName("correlation_id");
        row.Property(value => value.CausationId).HasColumnName("causation_id");
        row.Property(value => value.TraceParent).HasColumnName("trace_parent");
        row.Property(value => value.TraceState).HasColumnName("trace_state");
        row.HasIndex(value => value.MessageId).IsUnique();
        row.HasIndex(value => new
            {
                value.ScopeKey,
                value.DueAt,
                value.ScheduleId,
                value.OccurrenceId,
            })
            .HasFilter("state = 1");
        return row;
    }

    /// <summary>Registers typed operations and required due-intent/outbox validation; install ConfigureGloamingCapabilities on the scoped context.</summary>
    public static IServiceCollection AddPostgresDueIntents<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<PostgresDueIntents<TContext>>();
        services.AddOutboxValidation<TContext>();
        return services.TryAddGloamingCapability<TContext>(
            Capability,
            CapabilityPhase.Validate,
            // Outbox validation has its own binding; manual validators remain complete.
            (_, database) => DueIntentScope.For(database).Validate(database, includeOutbox: false),
            (_, database, token) =>
                DueIntentScope.For(database).ValidateAsync(database, token, includeOutbox: false)
        );
    }

    /// <summary>Validates tracked occurrences and their exact handoff pairs, including native reads for unchanged proposals; also validates the outbox.</summary>
    public static void ValidateDueIntentChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        DueIntentScope.For(database).Validate(database);
    }

    /// <summary>Cancellable native asynchronous validation for manual async-save integration, including unchanged-proposal reads.</summary>
    public static Task ValidateDueIntentChangesAsync(
        this DbContext database,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(database);

        return DueIntentScope.For(database).ValidateAsync(database, cancellationToken);
    }

    internal static void ValidateIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (
            identifier.Contains('\0', StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(identifier) > 63
        )
            throw new ArgumentException(
                "Use a PostgreSQL identifier of at most 63 UTF-8 bytes.",
                nameof(identifier)
            );
    }
}
