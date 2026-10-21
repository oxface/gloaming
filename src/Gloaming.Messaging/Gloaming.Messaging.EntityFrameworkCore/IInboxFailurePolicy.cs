using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Subscription-specific consumer classification and retry/stop policy.</summary>
/// <remarks>Decide synchronously without I/O or business mutation. Invalid decisions/errors abort accounting.</remarks>
public interface IInboxFailurePolicy<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Select retry eligibility or quarantine after local work has rolled back.</summary>
    InboxFailureDecision Decide(InboxFailure failure);
}
