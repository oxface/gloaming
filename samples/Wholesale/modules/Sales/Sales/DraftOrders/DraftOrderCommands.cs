using Gloaming.ActorIdentity;
using Gloaming.EventReactions;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

internal sealed class DraftOrderCommands(
    SalesDbContext database,
    IActorContextAccessor actor,
    DraftOrderAuthorization authorization,
    IStockCatalog catalog,
    IEventSink<SalesModule> events
) : IDraftOrderCommands
{
    public async Task<DraftOrderChangeResult> CreateAsync(
        CreateDraftOrder request,
        CancellationToken cancellationToken
    )
    {
        RequireWriteScope();
        DraftOrderAggregate candidate;
        try
        {
            candidate = DraftOrderAggregate.Create(request);
        }
        catch (OrderLimitExceededException exception)
        {
            return new DraftOrderChangeResult.LimitExceeded(exception.Limit, exception.Requested);
        }

        if (database.DraftOrders.Local.Any(row => row.Id == candidate.Id))
            throw new InvalidOperationException(
                "Use a fresh operation for an already tracked draft."
            );

        if (await database.DraftOrders.AnyAsync(row => row.Id == candidate.Id, cancellationToken))
            return new DraftOrderChangeResult.Conflict();

        if (
            !await database.Customers.AnyAsync(
                row => row.Id == candidate.CustomerId,
                cancellationToken
            )
        )
            return new DraftOrderChangeResult.CustomerNotFound();

        var missing = await MissingSkuAsync(
            candidate.Lines.Select(line => line.Sku),
            cancellationToken
        );
        if (missing is not null)
            return new DraftOrderChangeResult.UnknownSku(missing);

        database.DraftOrders.Add(DraftOrderRow.Create(database.RequiredOrganizationKey, candidate));
        database.DraftOrderGrants.Add(
            new DraftOrderGrantRow
            {
                OrganizationKey = database.RequiredOrganizationKey,
                OrderId = candidate.Id,
                SubjectKind = ActorKind.Human,
                SubjectKey = actor.Current.Actor.Id!.Value,
                Permission = DraftOrderAction.Edit,
            }
        );
        return new DraftOrderChangeResult.Changed(candidate.ToView());
    }

    public async Task<DraftOrderChangeResult> ChangeLinesAsync(
        ChangeDraftOrderLines request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.Id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ExpectedVersion, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(request.ExpectedVersion, long.MaxValue);
        ArgumentNullException.ThrowIfNull(request.Changes);

        DraftOrderLineChange[] changes = request.Changes.ToArray();
        ArgumentOutOfRangeException.ThrowIfLessThan(changes.Length, 1);

        RequireWriteScope();
        var row = await authorization
            .Allowed(DraftOrderAction.Edit)
            .Include(value => value.Lines)
            .AsSingleQuery()
            .SingleOrDefaultAsync(value => value.Id == request.Id, cancellationToken);
        if (row is null)
            return new DraftOrderChangeResult.NotFound();

        if (database.Entry(row).State != EntityState.Unchanged)
            throw new InvalidOperationException(
                "Save or discard the current proposal before another mutation."
            );

        if (row.Version != request.ExpectedVersion)
            return new DraftOrderChangeResult.Conflict();

        if (row.IsSubmitted)
            return new DraftOrderChangeResult.AlreadySubmitted();

        DraftOrderAggregate candidate;
        try
        {
            candidate = row.ToAggregate().ChangeLines(changes);
        }
        catch (OrderLimitExceededException exception)
        {
            return new DraftOrderChangeResult.LimitExceeded(exception.Limit, exception.Requested);
        }

        var existing = row.Lines.ToDictionary(line => line.Id);
        var missing = await MissingSkuAsync(
            candidate.Lines.Where(line => !existing.ContainsKey(line.Id)).Select(line => line.Sku),
            cancellationToken
        );
        if (missing is not null)
            return new DraftOrderChangeResult.UnknownSku(missing);

        row.Version = candidate.Version;
        database.Entry(row).Property(value => value.Version).OriginalValue =
            request.ExpectedVersion;
        foreach (
            var removed in row
                .Lines.Where(line => candidate.Lines.All(value => value.Id != line.Id))
                .ToArray()
        )
        {
            row.Lines.Remove(removed);
            database.DraftOrderLines.Remove(removed);
        }

        foreach (var line in candidate.Lines)
        {
            if (existing.TryGetValue(line.Id, out var saved))
            {
                saved.Quantity = line.Quantity;
                saved.UnitPrice = line.Price.Amount;
            }
            else
            {
                var added = DraftOrderLineRow.Create(row, line);
                row.Lines.Add(added);
                database.DraftOrderLines.Add(added);
            }
        }

        return new DraftOrderChangeResult.Changed(candidate.ToView());
    }

    public async Task<DraftOrderChangeResult> SubmitAsync(
        SubmitDraftOrder request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.Id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ExpectedVersion, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(request.ExpectedVersion, long.MaxValue);

        RequireWriteScope();
        var row = await authorization
            .Allowed(DraftOrderAction.Edit)
            .Include(value => value.Lines)
            .AsSingleQuery()
            .SingleOrDefaultAsync(value => value.Id == request.Id, cancellationToken);
        if (row is null)
            return new DraftOrderChangeResult.NotFound();

        const string operation = "submit-draft";
        string input = SalesCommandReceipts.Input(new { request.Id, request.ExpectedVersion });
        var receipt = request.OperationId is { } operationId
            ? await SalesCommandReceipts.AcquireAsync<DraftOrder>(
                database,
                operation,
                operationId,
                input,
                cancellationToken
            )
            : null;
        if (receipt?.Result is { } retained)
            return new DraftOrderChangeResult.Changed(retained);

        if (database.Entry(row).State != EntityState.Unchanged)
            throw new InvalidOperationException("Save the current proposal before submitting.");

        if (row.Version != request.ExpectedVersion)
            return new DraftOrderChangeResult.Conflict();

        if (row.IsSubmitted)
            return new DraftOrderChangeResult.AlreadySubmitted();

        var (candidate, submitted) = row.ToAggregate().Submit();
        row.Version = candidate.Version;
        row.IsSubmitted = candidate.IsSubmitted;
        events.Raise(submitted);
        receipt?.Retain(candidate.ToView());

        return new DraftOrderChangeResult.Changed(candidate.ToView());
    }

    private void RequireWriteScope()
    {
        _ = database.RequiredOrganizationKey;
        if (actor.Current.Actor.Kind != ActorKind.Human)
            throw new InvalidOperationException("Draft mutations require an admitted human actor.");

        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Start the owning Sales transaction before staging a draft."
            );
    }

    private async Task<string?> MissingSkuAsync(
        IEnumerable<string> skus,
        CancellationToken cancellationToken
    )
    {
        foreach (string sku in skus.Distinct(StringComparer.Ordinal))
        {
            if (await catalog.ReadAsync(sku, cancellationToken) is null)
                return sku;
        }

        return null;
    }
}
