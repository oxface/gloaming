using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.Sales;

// Sales owns classification and disclosure; callers supply the accepted change only.
internal sealed class SalesAudit(
    IAudit<SalesDbContext> audit,
    IActorContextAccessor actor,
    ITenantContextAccessor tenancy,
    TimeProvider timeProvider
)
{
    internal void ProfileChanged(
        Guid customerId,
        Guid addressId,
        long previousVersion,
        long version
    )
    {
        var now = timeProvider.GetUtcNow();
        audit.Stage(
            new AuditEntry(
                Guid.CreateVersion7(now),
                now,
                actor.Current,
                "sales",
                "customer-profile.changed",
                "customer",
                customerId.ToString("D"),
                "accepted",
                ProfileChangedDetailsV1.SchemaVersion,
                JsonSerializer.SerializeToElement(
                    new ProfileChangedDetailsV1(addressId, previousVersion, version)
                ),
                tenantKey: tenancy.Current.RequireTenant().Value
            )
        );
    }

    private sealed record ProfileChangedDetailsV1(
        Guid AddressId,
        long PreviousVersion,
        long Version
    )
    {
        internal const int SchemaVersion = 1;
    }
}
