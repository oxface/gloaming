namespace Gloaming.Samples.Wholesale.Access.Contracts;

// The caller supplies trusted identity; these reads do not authenticate or grant permissions.
public interface IApplicationAccess
{
    Task<UserId?> ResolveUserAsync(ExternalIdentity identity, CancellationToken cancellationToken);
    Task<OrganizationId?> ResolvePublicOrganizationAsync(
        string candidate,
        CancellationToken cancellationToken
    );
    Task<OrganizationId?> ResolveMemberOrganizationAsync(
        UserId user,
        string candidate,
        CancellationToken cancellationToken
    );
}
