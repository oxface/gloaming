using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Auditing.EntityFrameworkCore.Postgres;

/// <summary>Native PostgreSQL specialization of the provided audit model.</summary>
public static class PostgresAuditModelExtensions
{
    /// <summary>Maps the audit envelope with JSONB details. Ownership and query indexes remain consumer policy.</summary>
    public static EntityTypeBuilder<AuditRecord> ConfigurePostgresAudit(
        this ModelBuilder model,
        string schema,
        string table
    )
    {
        var row = model.ConfigureAudit(schema, table);
        row.Property(item => item.Details).HasColumnType("jsonb");
        return row;
    }
}
