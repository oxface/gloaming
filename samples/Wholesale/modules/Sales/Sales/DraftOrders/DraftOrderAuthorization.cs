using Gloaming.ActorIdentity;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

internal sealed class DraftOrderAuthorization(SalesDbContext database, IActorContextAccessor actor)
    : IDraftOrderAuthorization
{
    public Task<bool> IsAllowedAsync(
        Guid id,
        DraftOrderAction permission,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);

        return Allowed(permission).AnyAsync(row => row.Id == id, cancellationToken);
    }

    // EXISTS preserves one result per draft even when Read and Edit grants overlap.
    // Read includes Edit; Edit also permits submission, whose domain rules still apply.
    internal IQueryable<DraftOrderRow> Allowed(DraftOrderAction permission)
    {
        if (!Enum.IsDefined(permission))
            throw new ArgumentOutOfRangeException(nameof(permission));

        string organization = database.RequiredOrganizationKey;
        Actor subject = actor.Current.Actor;
        if (subject.Kind is not (ActorKind.Human or ActorKind.System))
            throw new InvalidOperationException("Private drafts require an identified actor.");

        string key = subject.Id!.Value;
        return database.DraftOrders.Where(row =>
            row.OrganizationKey == organization
            && database.DraftOrderGrants.Any(grant =>
                grant.OrganizationKey == row.OrganizationKey
                && grant.OrderId == row.Id
                && grant.SubjectKind == subject.Kind
                && grant.SubjectKey == key
                && (
                    grant.Permission == permission
                    || (
                        permission == DraftOrderAction.Read
                        && grant.Permission == DraftOrderAction.Edit
                    )
                )
            )
        );
    }
}
