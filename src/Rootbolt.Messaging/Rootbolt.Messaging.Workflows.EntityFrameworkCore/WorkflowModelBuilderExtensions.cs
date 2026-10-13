using Microsoft.EntityFrameworkCore;

namespace Rootbolt.Messaging.Workflows.EntityFrameworkCore;

/// <summary>Native EF mapping for a module-owned workflow table.</summary>
public static class WorkflowModelBuilderExtensions
{
    /// <summary>Configure provided records. JSON column type is explicit; PostgreSQL JSONB is the proven provider.</summary>
    public static ModelBuilder ConfigureWorkflows(
        this ModelBuilder model,
        string schema,
        string tableName,
        string jsonColumnType
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonColumnType);

        var row = model.Entity<WorkflowInstanceRecord>();
        row.ToTable(tableName, schema);
        row.HasKey(value => new { value.WorkflowType, value.Id });
        row.Property(value => value.WorkflowType).HasMaxLength(256);
        row.Property(value => value.Id).ValueGeneratedNever();
        row.Property(value => value.TenantKey).HasMaxLength(256).IsConcurrencyToken();
        row.Property(value => value.Version).IsConcurrencyToken().ValueGeneratedNever();
        row.Property(value => value.ContinuationName).HasMaxLength(256);
        row.Property(value => value.InitialInput).HasColumnType(jsonColumnType);
        row.Property(value => value.State).HasColumnType(jsonColumnType);
        row.Property(value => value.Operations).HasColumnType(jsonColumnType);
        return model;
    }
}
