using Gloaming.Samples.Wholesale.Inventory.StockPositions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gloaming.Samples.Wholesale.Inventory;

// Native composition policy: only these module stream constraints are version conflicts.
public static class InventoryAppendFailures
{
    public static bool IsVersionConflict(DbUpdateException failure) =>
        failure is DbUpdateConcurrencyException { Entries.Count: > 0 } concurrency
            && concurrency.Entries.All(entry =>
                entry.Entity is EventStream or StockPositionStateRow
            )
        || failure.InnerException
            is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                SchemaName: "inventory",
                ConstraintName: "PK_event_streams"
                    or "IX_events_organization_key_stream_id_stream_version"
            };
}
