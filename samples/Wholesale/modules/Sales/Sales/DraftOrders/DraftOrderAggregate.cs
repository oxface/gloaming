using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.Sales.DraftOrders;

internal sealed record OrderLine
{
    private OrderLine(Guid id, string sku, decimal quantity, Money price)
    {
        Id = id;
        Sku = sku;
        Quantity = quantity;
        Price = price;
    }

    internal Guid Id { get; }
    internal string Sku { get; }
    internal decimal Quantity { get; }
    internal Money Price { get; }

    internal static OrderLine Create(DraftOrderLine line, string currency)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfEqual(line.Id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(line.Sku);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line.Sku.Length, 128);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line.Quantity);

        return new(line.Id, line.Sku, line.Quantity, Money.Create(line.UnitPrice, currency));
    }

    internal DraftOrderLine ToView() => new(Id, Sku, Quantity, Price.Amount);
}

internal sealed class DraftOrderAggregate
{
    private DraftOrderAggregate(
        Guid id,
        Guid customerId,
        string code,
        long version,
        Money limit,
        IEnumerable<OrderLine> lines,
        bool isSubmitted = false
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code.Length, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(limit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit.Amount);
        ArgumentNullException.ThrowIfNull(lines);

        OrderLine[] candidate = lines.OrderBy(line => line.Id).ToArray();
        ArgumentOutOfRangeException.ThrowIfLessThan(candidate.Length, 1);
        if (candidate.Select(line => line.Id).Distinct().Count() != candidate.Length)
            throw new ArgumentException(
                "A draft must have distinct line identities.",
                nameof(lines)
            );

        if (candidate.Any(line => line.Price.Currency != limit.Currency))
            throw new ArgumentException(
                "All line prices must use the draft currency.",
                nameof(lines)
            );

        decimal total = candidate.Sum(line => checked(line.Quantity * line.Price.Amount));
        if (total > limit.Amount)
            throw new OrderLimitExceededException(limit.Amount, total);

        Id = id;
        CustomerId = customerId;
        Code = code;
        Version = version;
        Limit = limit;
        Total = Money.Create(total, limit.Currency);
        Lines = Array.AsReadOnly(candidate);
        IsSubmitted = isSubmitted;
    }

    internal Guid Id { get; }
    internal Guid CustomerId { get; }
    internal string Code { get; }
    internal long Version { get; }
    internal Money Limit { get; }
    internal Money Total { get; }
    internal IReadOnlyList<OrderLine> Lines { get; }
    internal bool IsSubmitted { get; }

    internal static DraftOrderAggregate Create(CreateDraftOrder request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        return new(
            request.Id,
            request.CustomerId,
            request.Code,
            1,
            Money.Create(request.TotalLimit, request.Currency),
            request.Lines.Select(line => OrderLine.Create(line, request.Currency))
        );
    }

    internal static DraftOrderAggregate FromState(
        Guid id,
        Guid customerId,
        string code,
        long version,
        string currency,
        decimal limit,
        IEnumerable<DraftOrderLine> lines,
        bool isSubmitted = false
    ) =>
        new(
            id,
            customerId,
            code,
            version,
            Money.Create(limit, currency),
            lines.Select(line => OrderLine.Create(line, currency)),
            isSubmitted
        );

    internal DraftOrderAggregate ChangeLines(IReadOnlyList<DraftOrderLineChange> changes)
    {
        if (IsSubmitted)
            throw new InvalidOperationException("A submitted draft cannot be edited.");

        ArgumentNullException.ThrowIfNull(changes);
        ArgumentOutOfRangeException.ThrowIfLessThan(changes.Count, 1);

        var candidate = Lines.ToDictionary(line => line.Id);
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            switch (change)
            {
                case DraftOrderLineChange.Add added:
                    var line = OrderLine.Create(added.Line, Limit.Currency);
                    if (Lines.Any(value => value.Id == line.Id))
                        throw new ArgumentException(
                            "Existing line identities cannot be reused by an Add operation.",
                            nameof(changes)
                        );

                    if (!candidate.TryAdd(line.Id, line))
                        throw new ArgumentException("The line already exists.", nameof(changes));

                    break;
                case DraftOrderLineChange.Change changed:
                    if (!candidate.TryGetValue(changed.LineId, out var current))
                        throw new ArgumentException("The line does not exist.", nameof(changes));

                    candidate[changed.LineId] = OrderLine.Create(
                        new(changed.LineId, current.Sku, changed.Quantity, changed.UnitPrice),
                        Limit.Currency
                    );
                    break;
                case DraftOrderLineChange.Remove removed:
                    if (!candidate.Remove(removed.LineId))
                        throw new ArgumentException("The line does not exist.", nameof(changes));

                    break;
                default:
                    throw new ArgumentException("Unknown line change.", nameof(changes));
            }
        }

        return new(Id, CustomerId, Code, checked(Version + 1), Limit, candidate.Values);
    }

    internal (DraftOrderAggregate State, DraftSubmitted Event) Submit()
    {
        if (IsSubmitted)
            throw new InvalidOperationException("The draft has already been submitted.");

        long nextVersion = checked(Version + 1);
        var state = new DraftOrderAggregate(Id, CustomerId, Code, nextVersion, Limit, Lines, true);
        return (state, new DraftSubmitted(Id, CustomerId, nextVersion, Total));
    }

    internal DraftOrder ToView() =>
        new(
            Id,
            CustomerId,
            Code,
            Version,
            Limit.Currency,
            Limit.Amount,
            Total.Amount,
            Array.AsReadOnly(Lines.Select(line => line.ToView()).ToArray()),
            IsSubmitted
        );
}

// Private accepted domain fact: neither an event-store record nor an integration contract.
internal sealed record DraftSubmitted(Guid OrderId, Guid CustomerId, long Version, Money Total);

internal sealed class OrderLimitExceededException(decimal limit, decimal requested)
    : Exception("The complete draft total exceeds its limit.")
{
    internal decimal Limit { get; } = limit;
    internal decimal Requested { get; } = requested;
}
