using ModulithFoundry.Samples.Wholesale.Access.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Access.Persistence;

internal sealed class UserRow
{
    public required string Id { get; init; }
}

internal sealed class ExternalIdentityRow
{
    public required string Issuer { get; init; }
    public required string Subject { get; init; }
    public required string UserId { get; init; }
}

internal sealed class OrganizationRow
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
}

internal sealed class MembershipRow
{
    public Guid Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string UserId { get; init; }
    public MembershipStatus Status { get; set; }
    public long Revision { get; set; } = 1;
    public bool CanAdministerMemberships { get; set; }
}
