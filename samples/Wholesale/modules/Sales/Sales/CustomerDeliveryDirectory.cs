using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales;

internal sealed class CustomerDeliveryPublication(IOutbox<SalesDbContext> outbox)
{
    internal static CustomerDeliveryState State(CustomerRow row, AddressRow address) =>
        new(
            row.Id,
            row.Version,
            row.DeliveryListed,
            row.DeliveryListed ? row.Code : null,
            row.DeliveryListed ? row.DisplayName : null,
            row.DeliveryListed ? address.AddressLine : null
        );

    internal void Enqueue(CustomerRow row, AddressRow address) =>
        outbox.Enqueue(
            OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                "dispatch.customer-directory",
                "sales.customer-delivery-state",
                1,
                State(row, address),
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                row.OrganizationKey
            )
        );
}

internal sealed class CustomerDeliveryDirectory(
    SalesDbContext database,
    IActorContextAccessor actor,
    CustomerDeliveryPublication publication,
    SalesAudit audit
) : ICustomerDeliveryDirectory
{
    public async Task<CustomerDeliveryPage> ReadPageAsync(
        Guid? after,
        int limit,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);

        _ = database.RequiredOrganizationKey;
        var query =
            from customer in database.Customers.AsNoTracking()
            join address in database.Addresses.AsNoTracking()
                on customer.Id equals address.CustomerId
            where !after.HasValue || customer.Id.CompareTo(after.Value) > 0
            orderby customer.Id
            select new { Customer = customer, Address = address };
        var rows = await query.Take(limit + 1).ToArrayAsync(token);
        var entries = rows.Take(limit)
            .Select(row => CustomerDeliveryPublication.State(row.Customer, row.Address))
            .ToArray();
        return new(
            database.RequiredOrganizationKey,
            entries,
            entries.Length == 0 ? after : entries[^1].CustomerId,
            rows.Length <= limit
        );
    }

    public async Task<CustomerProfile> RegisterAsync(
        RegisterDeliveryCustomer customer,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(customer);
        RequireHuman();

        var profile = CustomerProfileAggregate
            .Create(
                customer.CustomerId,
                customer.AddressId,
                customer.Code,
                CustomerProfileDetails.Create(customer.DisplayName, customer.AddressLine)
            )
            .ToProfile();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var row = new CustomerRow
        {
            Id = profile.CustomerId,
            OrganizationKey = database.RequiredOrganizationKey,
            Code = profile.Code,
            DisplayName = profile.DisplayName,
            Version = profile.Version,
        };
        var address = new AddressRow
        {
            Id = profile.AddressId,
            CustomerId = row.Id,
            OrganizationKey = row.OrganizationKey,
            AddressLine = profile.AddressLine,
        };
        database.Add(row);
        database.Add(address);
        publication.Enqueue(row, address);
        audit.DirectoryChanged(row.Id, row.Version, row.DeliveryListed, created: true);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return profile;
    }

    public async Task<ProfileChangeResult> SetListedAsync(
        Guid customerId,
        long expectedRevision,
        bool listed,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(expectedRevision, long.MaxValue);
        RequireHuman();

        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var customer = await database.Customers.SingleOrDefaultAsync(
            row => row.Id == customerId,
            token
        );
        var address = await database.Addresses.SingleOrDefaultAsync(
            row => row.CustomerId == customerId,
            token
        );
        if (customer is null || address is null)
            return new ProfileChangeResult.NotFound();

        if (customer.Version != expectedRevision)
            return new ProfileChangeResult.Conflict();

        customer.Version = checked(expectedRevision + 1);
        customer.DeliveryListed = listed;
        publication.Enqueue(customer, address);
        audit.DirectoryChanged(customer.Id, customer.Version, listed, created: false);
        try
        {
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new ProfileChangeResult.Updated(
                new(
                    customer.Id,
                    address.Id,
                    customer.Code,
                    customer.DisplayName,
                    address.AddressLine,
                    customer.Version
                )
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new ProfileChangeResult.Conflict();
        }
    }

    private void RequireHuman()
    {
        _ = database.RequiredOrganizationKey;
        if (actor.Current.Actor.Kind != ActorKind.Human)
            throw new InvalidOperationException(
                "Directory changes require an established human actor."
            );
    }
}
