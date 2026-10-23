using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Access.Persistence;

namespace Gloaming.Samples.Wholesale.Access;

internal sealed class MembershipAudit(
    IAudit<AccessDbContext> audit,
    IActorContextAccessor actor,
    TimeProvider timeProvider
)
{
    internal void Changed(MembershipRow membership, MembershipStatus previousStatus)
    {
        var now = timeProvider.GetUtcNow();
        audit.Stage(
            new AuditEntry(
                Guid.CreateVersion7(now),
                now,
                actor.Current,
                "access",
                membership.Status == MembershipStatus.Suspended
                    ? "membership.suspended"
                    : "membership.reactivated",
                "membership",
                membership.Id.ToString("D"),
                "accepted",
                MembershipChangedV1.SchemaVersion,
                JsonSerializer.SerializeToElement(
                    new MembershipChangedV1(
                        membership.UserId,
                        previousStatus,
                        membership.Status,
                        membership.Revision - 1,
                        membership.Revision
                    )
                ),
                tenantKey: membership.OrganizationId
            )
        );
    }

    private sealed record MembershipChangedV1(
        string UserId,
        MembershipStatus PreviousStatus,
        MembershipStatus Status,
        long PreviousRevision,
        long Revision
    )
    {
        internal const int SchemaVersion = 1;
    }
}
