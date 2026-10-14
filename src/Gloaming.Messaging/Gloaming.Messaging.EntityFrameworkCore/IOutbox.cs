using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Enqueues outgoing messages in one owning module's native EF context.</summary>
/// <typeparam name="TDbContext">The same typed context that tracks the producer's business changes.</typeparam>
public interface IOutbox<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Adds outgoing work to this context's explicit transaction. Does not save or publish.</summary>
    /// <remarks>The caller owns SaveChanges and commit. Use ValidateOutboxChanges in both native save overrides.</remarks>
    void Enqueue(OutgoingMessage message);
}
