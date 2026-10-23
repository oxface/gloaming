using System.Data;
using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Access.Persistence;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Access;

internal sealed class MembershipAdministration(
    AccessDbContext database,
    IActorContextAccessor actor,
    ITenantContextAccessor tenancy,
    MembershipAudit audit
) : IMembershipAdministration
{
    public async Task<OrganizationMembership?> ReadAsync(
        Guid membershipId,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(membershipId, Guid.Empty);

        var (organization, user) = RequireScope();
        if (!await CanAdministerAsync(organization, user, cancellationToken))
            return null;

        var membership = await database
            .Memberships.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.OrganizationId == organization && row.Id == membershipId,
                cancellationToken
            );
        return membership is null ? null : View(membership);
    }

    public Task<MembershipChangeResult> SuspendAsync(
        MembershipChange change,
        CancellationToken cancellationToken
    ) => ChangeAsync(change, MembershipStatus.Suspended, cancellationToken);

    public Task<MembershipChangeResult> ReactivateAsync(
        MembershipChange change,
        CancellationToken cancellationToken
    ) => ChangeAsync(change, MembershipStatus.Active, cancellationToken);

    private async Task<MembershipChangeResult> ChangeAsync(
        MembershipChange change,
        MembershipStatus requested,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentOutOfRangeException.ThrowIfEqual(change.MembershipId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(change.ExpectedRevision, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(change.ExpectedRevision, long.MaxValue);

        var (organization, user) = RequireScope();
        if (database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Use a fresh Access operation for administration.");

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        try
        {
            // One native Organization lock precedes all authority/target reads. Global
            // identity lookup still needs no tenant context and acquires no such lock.
            var selected = await database
                .Organizations.FromSqlInterpolated(
                    $"SELECT * FROM access.organizations WHERE id = {organization} FOR UPDATE"
                )
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (
                selected is null
                || !await CanAdministerAsync(organization, user, cancellationToken)
            )
                return new MembershipChangeResult.NotFound();

            // Database values, never a retained tracked target from an earlier decision.
            var membership = await database
                .Memberships.AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.OrganizationId == organization && row.Id == change.MembershipId,
                    cancellationToken
                );
            if (membership is null)
                return new MembershipChangeResult.NotFound();

            if (membership.Revision != change.ExpectedRevision)
                return new MembershipChangeResult.Conflict();

            if (membership.Status == MembershipStatus.Removed)
                return new MembershipChangeResult.InvalidTransition();

            if (membership.Status == requested)
                return new MembershipChangeResult.AlreadyInRequestedState(View(membership));

            if (requested == MembershipStatus.Suspended)
            {
                if (
                    membership.CanAdministerMemberships
                    && await database.Memberships.CountAsync(
                        row =>
                            row.OrganizationId == organization
                            && row.Status == MembershipStatus.Active
                            && row.CanAdministerMemberships,
                        cancellationToken
                    ) <= 1
                )
                    return new MembershipChangeResult.LastAdministrator();

                if (membership.UserId == user)
                    return new MembershipChangeResult.SelfSuspensionNotAllowed();
            }

            // Attach only this fresh read; caller state must not choose the original token.
            if (database.Memberships.Local.Any(row => row.Id == membership.Id))
                throw new InvalidOperationException("Use a fresh Access operation for the target.");

            database.Attach(membership);
            MembershipStatus previous = membership.Status;
            membership.Status = requested;
            membership.Revision++;
            audit.Changed(membership, previous);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MembershipChangeResult.Changed(View(membership));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new MembershipChangeResult.Conflict();
        }
    }

    private (string Organization, string User) RequireScope()
    {
        string organization = tenancy.Current.RequireTenant().Value;
        Actor current = actor.Current.Actor;
        if (current.Kind != ActorKind.Human)
            throw new InvalidOperationException(
                "Membership administration requires a human actor."
            );

        return (organization, current.Id!.Value);
    }

    private Task<bool> CanAdministerAsync(
        string organization,
        string user,
        CancellationToken cancellationToken
    ) =>
        database
            .Memberships.AsNoTracking()
            .AnyAsync(
                row =>
                    row.OrganizationId == organization
                    && row.UserId == user
                    && row.Status == MembershipStatus.Active
                    && row.CanAdministerMemberships,
                cancellationToken
            );

    private static OrganizationMembership View(MembershipRow row) =>
        new(row.Id, new UserId(row.UserId), row.Status, row.Revision, row.CanAdministerMemberships);
}
