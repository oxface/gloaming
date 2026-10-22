namespace Gloaming.Tenancy;

/// <summary>A consumer-supplied tenant key, preserved and compared exactly.</summary>
public sealed record TenantId
{
    /// <summary>Preserves a nonblank opaque tenant key without trimming, parsing or case conversion.</summary>
    public TenantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The original opaque tenant key, compared with ordinal semantics.</summary>
    public string Value { get; }
}
