using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Consumer classification and retry/stop policy for one context's outgoing work.</summary>
/// <remarks>Decide synchronously without I/O or business mutation. Exceptions do not prove transport rejection.</remarks>
public interface IOutboxFailurePolicy<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Select eligibility after a publisher exception while its claim remains valid.</summary>
    OutboxFailureDecision Decide(OutboxFailure failure);
}
