using System.Text;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>Explicit model, registration and native saving integration; never starts workers or commits.</summary>
public static class CommandReceiptConfiguration
{
    internal const string Capability = "command-receipts.postgres.v1";

    /// <summary>Maps the provided record at a consumer-selected schema/table. Scope column naming and native ownership/RLS remain composable.</summary>
    public static EntityTypeBuilder<CommandReceiptRecord> ConfigureCommandReceipts(
        this ModelBuilder model,
        string schema,
        string table = "command_receipts"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateIdentifier(schema);
        ValidateIdentifier(table);

        model.RequireGloamingCapability(Capability);
        var row = model.Entity<CommandReceiptRecord>();
        row.HasAnnotation(Capability, true);
        row.ToTable(
            table,
            schema,
            mapping => mapping.HasCheckConstraint("ck_command_receipt_format", "format_version > 0")
        );
        row.HasKey(value => new
        {
            value.ScopeKey,
            value.Operation,
            value.OperationId,
        });
        row.Property(value => value.ScopeKey)
            .HasColumnName("scope_key")
            .HasMaxLength(256)
            .UseCollation("C");
        row.Property(value => value.Operation)
            .HasColumnName("operation")
            .HasMaxLength(128)
            .UseCollation("C");
        row.Property(value => value.OperationId)
            .HasColumnName("operation_id")
            .ValueGeneratedNever();
        row.Property(value => value.FormatVersion).HasColumnName("format_version");
        row.Property(value => value.Input).HasColumnName("input").HasColumnType("text");
        row.Property(value => value.Result).HasColumnName("result").HasColumnType("text");
        return row;
    }

    /// <summary>Registers a context-typed store and required save validation. Install ConfigureGloamingCapabilities on that scoped context.</summary>
    public static IServiceCollection AddPostgresCommandReceipts<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<PostgresCommandReceipts<TContext>>();
        return services.TryAddGloamingCapability<TContext>(
            Capability,
            CapabilityPhase.Validate,
            (_, database) => database.ValidateCommandReceiptChanges()
        );
    }

    /// <summary>Alternative explicit save integration; call for both sync and async saves after detecting changes.</summary>
    public static void ValidateCommandReceiptChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.ChangeTracker.DetectChanges();
        ReceiptScope.For(database).Validate(database);
    }

    internal static void ValidateIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (
            identifier.Contains('\0', StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(identifier) > 63
        )
            throw new ArgumentException(
                "Use an explicit PostgreSQL identifier of at most 63 UTF-8 bytes; truncation would split lock identity.",
                nameof(identifier)
            );
    }
}
