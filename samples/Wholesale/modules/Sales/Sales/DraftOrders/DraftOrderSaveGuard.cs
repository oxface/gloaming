using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

internal static class DraftOrderSaveGuard
{
    internal static void ValidateDraftOrderChanges(this SalesDbContext database)
    {
        database.ChangeTracker.DetectChanges();
        var roots = database
            .ChangeTracker.Entries<DraftOrderRow>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();
        foreach (var root in roots)
        {
            var row = root.Entity;
            if (root.State == EntityState.Added)
            {
                if (row.Version != 1)
                    throw new InvalidOperationException("A new draft must start at version 1.");
            }
            else
            {
                if (row.Version != checked(root.Property(value => value.Version).OriginalValue + 1))
                    throw new InvalidOperationException(
                        "Every draft mutation must advance the root version once."
                    );

                if (!root.Collection(value => value.Lines).IsLoaded)
                    throw new InvalidOperationException(
                        "Load the complete draft before changing its children."
                    );

                if (
                    root.Property(value => value.CustomerId).IsModified
                    || root.Property(value => value.Code).IsModified
                    || root.Property(value => value.Currency).IsModified
                    || root.Property(value => value.TotalLimit).IsModified
                )
                    throw new InvalidOperationException(
                        "Draft identity, customer, currency and limit are fixed after creation."
                    );
            }

            _ = row.ToAggregate(
                row.Lines.Where(line => database.Entry(line).State != EntityState.Deleted)
            );
        }

        foreach (
            var child in database
                .ChangeTracker.Entries<DraftOrderLineRow>()
                .Where(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                )
        )
        {
            var parent = roots.SingleOrDefault(root =>
                root.Entity.Id == child.Entity.OrderId
                && root.Entity.OrganizationKey == child.Entity.OrganizationKey
            );
            if (parent is null)
                throw new InvalidOperationException(
                    "Child writes require a participating versioned draft root."
                );

            if (child.State == EntityState.Modified && child.Property(line => line.Sku).IsModified)
                throw new InvalidOperationException(
                    "Replace a line to change its catalog reference."
                );

            if (child.State != EntityState.Deleted && !parent.Entity.Lines.Contains(child.Entity))
                throw new InvalidOperationException(
                    "The changed child must belong to the complete draft graph."
                );
        }
    }
}
