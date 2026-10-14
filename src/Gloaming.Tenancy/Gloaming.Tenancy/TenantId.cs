namespace Gloaming.Tenancy;

/// <summary>A consumer-supplied tenant key, preserved and compared exactly.</summary>
public sealed record TenantId
{
    public TenantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
