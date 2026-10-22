namespace Gloaming.ActorIdentity;

/// <summary>A consumer-supplied actor key, preserved and compared exactly.</summary>
public sealed record ActorId
{
    /// <summary>Preserves a nonblank opaque actor key without trimming, parsing or case conversion.</summary>
    public ActorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The original opaque actor key, compared with ordinal semantics.</summary>
    public string Value { get; }
}
