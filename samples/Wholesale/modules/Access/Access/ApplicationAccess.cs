using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Access.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Access;

internal sealed class ApplicationAccess(AccessDbContext database) : IApplicationAccess
{
    public async Task<UserId?> ResolveUserAsync(
        ExternalIdentity identity,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(identity);
        string? user = await database
            .ExternalIdentities.AsNoTracking()
            .Where(row => row.Issuer == identity.Issuer && row.Subject == identity.Subject)
            .Select(row => row.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        return user is null ? null : new UserId(user);
    }

    public async Task<OrganizationId?> ResolvePublicOrganizationAsync(
        string candidate,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        string? slug = OrganizationSlug.Canonicalize(candidate);
        if (slug is null)
            return null;
        string? organization = await database
            .Organizations.AsNoTracking()
            .Where(row => row.Slug == slug)
            .Select(row => row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return organization is null ? null : new OrganizationId(organization);
    }

    public async Task<OrganizationId?> ResolveMemberOrganizationAsync(
        UserId user,
        string candidate,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        string? slug = OrganizationSlug.Canonicalize(candidate);
        if (slug is null)
            return null;
        string? organization = await (
            from membership in database.Memberships.AsNoTracking()
            join selected in database.Organizations.AsNoTracking()
                on membership.OrganizationId equals selected.Id
            where
                membership.UserId == user.Value
                && membership.Status == MembershipStatus.Active
                && selected.Slug == slug
            select selected.Id
        ).SingleOrDefaultAsync(cancellationToken);
        return organization is null ? null : new OrganizationId(organization);
    }
}
