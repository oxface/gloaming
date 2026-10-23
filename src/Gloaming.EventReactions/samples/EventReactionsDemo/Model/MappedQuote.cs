namespace Gloaming.Samples.EventReactions.Model;

public sealed record QuoteAccepted(Guid Id, decimal Amount);

public sealed record QuoteAllowanceAllocated(Guid Id, decimal Amount);

public sealed record QuoteDeleted(Guid Id, decimal Amount);

// Consumer-owned decisions and facts have no library or persistence dependency.
public sealed record QuoteDomain(Guid Id, decimal Amount, bool Accepted, long Version)
{
    public (QuoteDomain State, QuoteAccepted Fact) Accept()
    {
        if (Accepted || Amount <= 0 || Version < 1)
            throw new InvalidOperationException("The quote is not an acceptable draft.");

        return (this with { Accepted = true, Version = checked(Version + 1) }, new(Id, Amount));
    }

    public QuoteDeleted Delete()
    {
        if (!Accepted)
            throw new InvalidOperationException("This example deletes previously accepted quotes.");

        return new(Id, Amount);
    }
}

public sealed record AllowanceDomain(decimal Ceiling, decimal Reserved, long Version)
{
    public (AllowanceDomain State, QuoteAllowanceAllocated Fact) Reserve(Guid id, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        decimal next = checked(Reserved + amount);
        if (next > Ceiling)
            throw new AllowanceExceededException(Ceiling, next);

        return (this with { Reserved = next, Version = checked(Version + 1) }, new(id, amount));
    }

    public AllowanceDomain Release(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (amount > Reserved)
            throw new InvalidOperationException(
                "The allocation cannot exceed the reserved allowance."
            );

        return this with
        {
            Reserved = Reserved - amount,
            Version = checked(Version + 1),
        };
    }
}
