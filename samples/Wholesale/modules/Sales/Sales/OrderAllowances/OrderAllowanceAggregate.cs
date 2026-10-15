namespace ModulithFoundry.Samples.Wholesale.Sales.OrderAllowances;

internal sealed class OrderAllowanceAggregate
{
    internal OrderAllowanceAggregate(Guid customerId, Money ceiling, Money reserved, long version)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling.Amount);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (reserved.Currency != ceiling.Currency || reserved.Amount > ceiling.Amount)
            throw new ArgumentException(
                "The reserved amount must fit the allowance currency and ceiling."
            );

        CustomerId = customerId;
        Ceiling = ceiling;
        Reserved = reserved;
        Version = version;
    }

    internal Guid CustomerId { get; }
    internal Money Ceiling { get; }
    internal Money Reserved { get; }
    internal long Version { get; }

    internal OrderAllowanceAggregate Reserve(Money amount)
    {
        if (amount.Currency != Ceiling.Currency)
            throw new ArgumentException("The submission must use the allowance currency.");

        decimal next = checked(Reserved.Amount + amount.Amount);
        if (next > Ceiling.Amount)
            throw new OrderAllowanceExceededException(CustomerId, Ceiling.Amount, next);

        return new(CustomerId, Ceiling, Money.Create(next, Ceiling.Currency), checked(Version + 1));
    }
}
