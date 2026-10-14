using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Consumer-owned decoding, admission and bounded local effects in the processor's transaction.</summary>
/// <remarks>Use the owning scoped context; do not commit, replace the transaction or perform external effects. Enqueue outgoing work instead.</remarks>
public interface IInboxHandler<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Stages local effects; normal return allows the processor to save and complete this delivery.</summary>
    Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken);
}
