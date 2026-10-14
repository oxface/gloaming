namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

// Sales owns this bounded currency representation; no currency catalogue or conversion is implied.
internal sealed record Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    internal decimal Amount { get; }
    internal string Currency { get; }

    internal static Money Create(decimal amount, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException(
                "Use a three-letter uppercase currency code.",
                nameof(currency)
            );

        return new(amount, currency);
    }
}
