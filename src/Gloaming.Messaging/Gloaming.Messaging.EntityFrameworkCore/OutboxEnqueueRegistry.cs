using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore;

internal static class OutboxEnqueueRegistry
{
    // Evidence follows row object identity, not MessageId or the lifetime of a pooled context.
    // Weak keys avoid retaining rows after their tracker and callers release them.
    private static readonly ConditionalWeakTable<OutboxMessageRecord, Registration> Registrations =
        new();

    internal static void Register(
        DbContext database,
        OutboxMessageRecord row,
        OutgoingMessage message,
        IDbContextTransaction transaction
    ) => Registrations.Add(row, new(database.ContextId, message, transaction));

    internal static Registration? Find(DbContext database, OutboxMessageRecord row) =>
        Registrations.TryGetValue(row, out var registration)
        && registration.ContextId == database.ContextId
            ? registration
            : null;

    // Keep evidence after validation: SQL may fail and the same transaction may retry.
    // ContextId includes the pool lease without adding a strong reference to the context.
    internal sealed record Registration(
        DbContextId ContextId,
        OutgoingMessage Message,
        IDbContextTransaction Transaction
    );
}
