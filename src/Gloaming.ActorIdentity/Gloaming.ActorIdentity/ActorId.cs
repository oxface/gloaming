namespace Gloaming.ActorIdentity;

/// <summary>A consumer-supplied actor key, preserved and compared exactly.</summary>
public sealed record ActorId
{
    public ActorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
