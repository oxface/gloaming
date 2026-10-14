using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

// Inventory owns accepted-issue classification and its minimal version/quantity payload.
internal sealed class InventoryAudit(
    IAudit<InventoryDbContext> audit,
    IActorContextAccessor actor,
    ITenantContextAccessor tenancy,
    TimeProvider timeProvider
)
{
    internal void StockIssued(Guid stockPositionId, long version, decimal quantity)
    {
        var now = timeProvider.GetUtcNow();
        audit.Stage(
            new AuditEntry(
                Guid.CreateVersion7(now),
                now,
                actor.Current,
                "inventory",
                "stock-position.issued",
                "stock-position",
                stockPositionId.ToString("D"),
                "accepted",
                StockIssuedDetailsV1.SchemaVersion,
                JsonSerializer.SerializeToElement(new StockIssuedDetailsV1(version, quantity)),
                tenantKey: tenancy.Current.RequireTenant().Value
            )
        );
    }

    private sealed record StockIssuedDetailsV1(long Version, decimal Quantity)
    {
        internal const int SchemaVersion = 1;
    }
}
