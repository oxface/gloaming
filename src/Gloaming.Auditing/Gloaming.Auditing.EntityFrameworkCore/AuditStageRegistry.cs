using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Auditing.EntityFrameworkCore;

internal static class AuditStageRegistry
{
    // Attach validation evidence to the row instance, not its ID or a DbContext cache.
    // Weak keys let the row and evidence be collected after tracker/callers release them.
    // ContextId includes the pool lease; a transferred row cannot acquire new evidence.
    private static readonly ConditionalWeakTable<AuditRecord, Registration> Registrations = new();

    internal static void Register(
        DbContext database,
        AuditRecord row,
        AuditEntry entry,
        IDbContextTransaction transaction
    ) => Registrations.Add(row, new(database.ContextId, entry, transaction));

    internal static Registration? Find(DbContext database, AuditRecord row) =>
        Registrations.TryGetValue(row, out var registration)
        && registration.ContextId == database.ContextId
            ? registration
            : null;

    // Retain the exact transaction wrapper for reference comparison, not reuse. Its
    // owner still disposes/commits it; a saved row's evidence is not a new transaction.
    internal sealed record Registration(
        DbContextId ContextId,
        AuditEntry Entry,
        IDbContextTransaction Transaction
    );
}
