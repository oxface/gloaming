namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>Opaque, immutable consumer result with an explicitly selected reader version.</summary>
public sealed record CommandReceiptContent
{
    /// <summary>The library stores text without selecting a serializer or payload semantics.</summary>
    public CommandReceiptContent(int formatVersion, string payload)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(formatVersion, 1);
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("PostgreSQL text cannot contain NUL.", nameof(payload));

        FormatVersion = formatVersion;
        Payload = payload;
    }

    /// <summary>Consumer-owned format identity; unknown versions must not cause re-execution.</summary>
    public int FormatVersion { get; }

    /// <summary>Retained business result or reference, not an HTTP response snapshot.</summary>
    public string Payload { get; }
}
