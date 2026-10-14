using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Auditing.EntityFrameworkCore;

/// <summary>Associates an explicitly selected audit with its owning native context and transaction.</summary>
public sealed class EfAudit<TDbContext> : IAudit<TDbContext>
    where TDbContext : DbContext
{
    private readonly TDbContext database;

    /// <summary>Requires explicit model configuration; opens no connection.</summary>
    public EfAudit(TDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (
            database
                .Model.FindEntityType(typeof(AuditRecord))
                ?.FindAnnotation("Gloaming:Audit")
                ?.Value
            is not true
        )
            throw new InvalidOperationException(
                "Configure this context's audit model before staging."
            );

        this.database = database;
    }

    /// <inheritdoc />
    public void Stage(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var transaction = database.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().Connection is null)
            throw new InvalidOperationException(
                "Audit staging requires an active native EF transaction."
            );

        var row = AuditRecord.Create(entry);
        AuditStageRegistry.Register(database, row, entry, transaction);
        database.Set<AuditRecord>().Add(row);
    }
}
