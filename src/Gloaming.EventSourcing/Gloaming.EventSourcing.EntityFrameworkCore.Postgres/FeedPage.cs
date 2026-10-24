using System.Text.Json;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

/// <summary>Retained v1 ordering position. A cursor conveys progress, never authority.</summary>
public sealed class FeedCursor : IEquatable<FeedCursor>
{
    private readonly string? retained;

    private FeedCursor(CursorData data, string? retained = null)
    {
        Data = data;
        this.retained = retained;
    }

    internal CursorData Data { get; }

    /// <summary>Opaque versioned retained progress value.</summary>
    public string Value =>
        retained ?? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Data));

    /// <summary>Begins before the selected feed's synthetic baseline.</summary>
    public static FeedCursor Start(Guid feedId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(feedId, Guid.Empty);

        return Fence(feedId, 0);
    }

    /// <summary>Restores a previously returned cursor; accepting caller-chosen progress can skip facts.</summary>
    public static FeedCursor Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        // Two native512-byte keys can expand sixfold under JSON escaping, then base64 adds4/3.
        if (value.Length > 16384)
            throw new ArgumentException("Invalid feed cursor length.", nameof(value));

        var data =
            JsonSerializer.Deserialize<CursorData>(Convert.FromBase64String(value))
            ?? throw new ArgumentException("Missing feed cursor.", nameof(value));
        if (
            data.Protocol != 1
            || data.FeedId == Guid.Empty
            || data.Position < 0
            || (
                data.Source is null
                && (data.Stream is not null || data.Event is not null || data.Version != 0)
            )
            || (
                data.Source is not null
                && (
                    data.Position == 0
                    || data.Version <= 0
                    || data.Stream is null
                    || data.Event is null
                )
            )
        )
            throw new ArgumentException("Unsupported feed cursor.", nameof(value));

        if (data.Source is not null)
        {
            EventFeedLocation.Identifier(data.Source);
            using var stream = JsonDocument.Parse(data.Stream!);
            using var key = JsonDocument.Parse(data.Event!);
            if (
                stream.RootElement.ValueKind != JsonValueKind.Array
                || key.RootElement.ValueKind != JsonValueKind.Array
            )
                throw new ArgumentException("Invalid retained feed key.", nameof(value));
        }

        return new(data, value);
    }

    /// <inheritdoc />
    public bool Equals(FeedCursor? other) => other is not null && Data == other.Data;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FeedCursor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Data.GetHashCode();

    internal static FeedCursor Fence(Guid feed, long position) =>
        new(new(1, feed, position, null, null, 0, null));

    internal static FeedCursor After(Guid feed, FeedEvent item) =>
        new(
            new(
                1,
                feed,
                item.Position,
                item.Source,
                item.StreamKey.GetRawText(),
                item.Version,
                item.EventKey.GetRawText()
            )
        );

    internal sealed record CursorData(
        int Protocol,
        Guid FeedId,
        long Position,
        string? Source,
        string? Stream,
        long Version,
        string? Event
    );
}

/// <summary>Bounds returned facts and their native UTF-8 JSONB envelope representation.</summary>
public sealed class FeedLimits
{
    /// <summary>Validates one to4096 events and one byte to16MiB of native JSON envelope output.</summary>
    public FeedLimits(int maxEvents, long maxPayloadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEvents, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEvents, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPayloadBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPayloadBytes, 16 * 1024 * 1024);

        MaxEvents = maxEvents;
        MaxPayloadBytes = maxPayloadBytes;
    }

    /// <summary>Maximum returned event count.</summary>
    public int MaxEvents { get; }

    /// <summary>Maximum total UTF-8 JSONB envelope bytes, excluding keys/protocol metadata.</summary>
    public long MaxPayloadBytes { get; }
}

/// <summary>Immutable native source-row copy; the consumer owns payload names, schemas and readers.</summary>
public sealed record FeedEvent(
    long Position,
    string Source,
    JsonElement StreamKey,
    long Version,
    JsonElement EventKey,
    JsonElement Envelope
);

/// <summary>A bounded committed page, retained continuation and observed committed head.</summary>
public sealed record FeedPage(
    IReadOnlyList<FeedEvent> Events,
    FeedCursor Next,
    long ObservedHead,
    bool HasMore
);

/// <summary>The first retained event exceeds this turn's byte bound; no progress was made.</summary>
public sealed class FeedEventTooLargeException(
    long position,
    string source,
    string eventKey,
    long bytes
)
    : InvalidOperationException(
        $"Feed event at position{position} in {source} requires{bytes} JSON bytes."
    )
{
    /// <summary>Retained transaction position.</summary>
    public long Position { get; } = position;

    /// <summary>Retained source identity.</summary>
    public string SourceName { get; } = source;

    /// <summary>Complete retained event key as native JSON.</summary>
    public string EventKey { get; } = eventKey;

    /// <summary>Required native UTF-8 JSON envelope bytes.</summary>
    public long Bytes { get; } = bytes;
}
