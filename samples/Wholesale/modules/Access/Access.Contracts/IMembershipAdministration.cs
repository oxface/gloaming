namespace Gloaming.Samples.Wholesale.Access.Contracts;

// The host establishes trusted actor/Organization context. Access reauthorizes each call.
public interface IMembershipAdministration
{
    Task<OrganizationMembership?> ReadAsync(Guid membershipId, CancellationToken cancellationToken);
    Task<MembershipChangeResult> SuspendAsync(
        MembershipChange change,
        CancellationToken cancellationToken
    );
    Task<MembershipChangeResult> ReactivateAsync(
        MembershipChange change,
        CancellationToken cancellationToken
    );
}

public enum MembershipStatus
{
    Active = 1,
    Suspended = 2,
    Removed = 3,
}

public sealed record OrganizationMembership(
    Guid Id,
    UserId User,
    MembershipStatus Status,
    long Revision,
    bool CanAdministerMemberships
);

public sealed record MembershipChange(Guid MembershipId, long ExpectedRevision);

public abstract record MembershipChangeResult
{
    public sealed record Changed(OrganizationMembership Membership) : MembershipChangeResult;

    public sealed record AlreadyInRequestedState(OrganizationMembership Membership)
        : MembershipChangeResult;

    public sealed record NotFound : MembershipChangeResult;

    public sealed record Conflict : MembershipChangeResult;

    public sealed record InvalidTransition : MembershipChangeResult;

    public sealed record SelfSuspensionNotAllowed : MembershipChangeResult;

    public sealed record LastAdministrator : MembershipChangeResult;
}
