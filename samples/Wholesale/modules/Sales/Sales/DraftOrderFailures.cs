using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;
using ModulithFoundry.Samples.Wholesale.Sales.OrderAllowances;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.Sales;

// Native save failures belong to the caller; unrelated constraints remain faults.
public static class DraftOrderFailures
{
    public static bool IsVersionConflict(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DbUpdateConcurrencyException)
            return exception.Entries.Count > 0
                && exception.Entries.All(entry =>
                    entry.Entity is DraftOrderRow or DraftOrderLineRow or OrderAllowanceRow
                );

        return exception.InnerException
            is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                SchemaName: "sales",
                ConstraintName: "pk_draft_orders" or "pk_draft_order_lines"
            };
    }
}
