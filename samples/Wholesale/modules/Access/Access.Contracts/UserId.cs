namespace Gloaming.Samples.Wholesale.Access.Contracts;

public sealed record UserId
{
    public UserId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
