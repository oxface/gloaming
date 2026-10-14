using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Tracks provided outbox records in the caller's native EF transaction.</summary>
/// <typeparam name="TDbContext">The owning producer context, also tracking the business changes.</typeparam>
public sealed class EfOutbox<TDbContext> : IOutbox<TDbContext>
    where TDbContext : DbContext
{
    private readonly TDbContext database;

    /// <summary>Validates the configured EF model without opening a database connection.</summary>
    public EfOutbox(TDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        var entity = database.Model.FindEntityType(typeof(OutboxMessageRecord));
        if (entity?.FindAnnotation("Gloaming:Outbox")?.Value is not true)
            throw new InvalidOperationException(
                "Configure this context's native outbox model before enqueueing."
            );
    }

    /// <inheritdoc />
    public void Enqueue(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var transaction =
            database.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Enqueue requires the caller's explicit native EF transaction."
            );
        var row = OutboxMessageRecord.Create(message);
        OutboxEnqueueRegistry.Register(database, row, message, transaction);
        database.Set<OutboxMessageRecord>().Add(row);
    }
}
