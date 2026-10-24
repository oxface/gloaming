using System.Collections.ObjectModel;
using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>The complete technical work/generation identity. It carries no participant or business authority.</summary>
public sealed record ContributionSetKey
{
    /// <summary>Ordinal consumer scope, independent of envelope tenancy.</summary>
    public string ScopeKey { get; }

    /// <summary>Consumer-selected logical work identity.</summary>
    public Guid WorkId { get; }

    /// <summary>Retained immutable generation; never reused after completion or timeout.</summary>
    public Guid GenerationId { get; }

    /// <summary>Validates the complete identity without I/O.</summary>
    public ContributionSetKey(string scopeKey, Guid workId, Guid generationId)
    {
        var finite = new DueIntentKey(scopeKey, workId, generationId);
        ScopeKey = finite.ScopeKey;
        WorkId = finite.ScheduleId;
        GenerationId = finite.OccurrenceId;
    }

    internal DueIntentKey TimerKey => new(ScopeKey, WorkId, GenerationId);
}

/// <summary>An owned immutable named/versioned JSON contribution with recursively unique object properties. Equality compares contract identity and JSON values.</summary>
public sealed class ContributionValue
{
    /// <summary>Stable consumer-selected content contract name.</summary>
    public string Name { get; }

    /// <summary>Positive retained content schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Owned opaque non-null JSON. Business meaning remains with its reader.</summary>
    public JsonElement Payload { get; }

    /// <summary>Clones a complete non-null contribution independently of the source document lifetime.</summary>
    public ContributionValue(string name, int schemaVersion, JsonElement payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ArgumentException("A contribution requires non-null JSON.", nameof(payload));

        PostgresJsonValues.RequireUniqueProperties(payload);

        Name = name;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    internal bool Same(ContributionValue other) =>
        Name == other.Name
        && SchemaVersion == other.SchemaVersion
        && JsonElement.DeepEquals(Payload, other.Payload);
}

/// <summary>An explicitly selected finite timeout signal and a separate terminal outcome envelope.</summary>
public sealed class ContributionTimeout
{
    /// <summary>Explicit UTC microsecond eligibility time; arrival alone does not read a clock.</summary>
    public DateTimeOffset DueAt { get; }

    /// <summary>Immutable finite signal. Its receiver invokes the timeout operation after checking consumer relevance.</summary>
    public OutgoingMessage Signal { get; }

    /// <summary>Immutable outcome staged only if the eligible timeout wins the generation lock.</summary>
    public OutgoingMessage Outcome { get; }

    /// <summary>Validates independent retained signal/outcome identities.</summary>
    public ContributionTimeout(
        DateTimeOffset dueAt,
        OutgoingMessage signal,
        OutgoingMessage outcome
    )
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(outcome);
        DueIntentStorage.RequireInstant(dueAt);
        PostgresJsonValues.RequireUniqueProperties(signal.Payload);
        PostgresJsonValues.RequireUniqueProperties(outcome.Payload);
        if (signal.MessageId == outcome.MessageId)
            throw new ArgumentException(
                "Use independent timeout signal and outcome delivery identities."
            );

        DueAt = dueAt;
        Signal = signal;
        Outcome = outcome;
    }
}

/// <summary>An immutable explicit expected set and its selected terminal notices. Participant/quorum/business policy is consumer-owned.</summary>
public sealed class ContributionSetDefinition
{
    /// <summary>Frozen ordinal contributor identities admitted to this technical generation.</summary>
    public ReadOnlyCollection<string> Expected { get; }

    /// <summary>Consumer-selected minimum distinct contributions for technical completion; no business quorum policy is inferred.</summary>
    public int Required { get; }

    /// <summary>Original immutable completion notice. It does not itself approve any business outcome.</summary>
    public OutgoingMessage Completion { get; }

    /// <summary>Explicit optional deadline signal and outcome; no ambient clock or worker is installed.</summary>
    public ContributionTimeout? Timeout { get; }

    /// <summary>Freezes 1..100 distinct nonblank contributor identities, each at most 256 .NET characters.</summary>
    public ContributionSetDefinition(
        IEnumerable<string> expected,
        int required,
        OutgoingMessage completion,
        ContributionTimeout? timeout
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(completion);
        PostgresJsonValues.RequireUniqueProperties(completion.Payload);

        string[] identities = expected.Take(101).ToArray();
        if (identities.Length is < 1 or > 100)
            throw new ArgumentException(
                "Select 1..100 explicit expected identities.",
                nameof(expected)
            );

        foreach (string identity in identities)
            Contributor(identity);

        ArgumentOutOfRangeException.ThrowIfLessThan(required, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(required, identities.Length);

        if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Length)
            throw new ArgumentException(
                "Expected contributor identities must be ordinally distinct.",
                nameof(expected)
            );

        if (
            timeout is not null
            && (
                completion.MessageId == timeout.Signal.MessageId
                || completion.MessageId == timeout.Outcome.MessageId
            )
        )
            throw new ArgumentException(
                "Use independent completion, timeout signal and timeout outcome delivery identities.",
                nameof(timeout)
            );

        Array.Sort(identities, StringComparer.Ordinal);
        Expected = Array.AsReadOnly(identities);
        Required = required;
        Completion = completion;
        Timeout = timeout;
    }

    internal static void Contributor(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(identity.Length, 256);

        if (identity.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException(
                "A PostgreSQL contributor identity cannot contain NUL.",
                nameof(identity)
            );
    }
}

/// <summary>Persisted technical generation state; business approval and relevance are separate.</summary>
public enum ContributionSetState
{
    /// <summary>Expected contributions are still accepted.</summary>
    Open = 1,

    /// <summary>The consumer-selected required distinct contribution count and one notice committed together.</summary>
    Completed = 2,

    /// <summary>An eligible explicit timeout and its selected outcome won before technical completion.</summary>
    TimedOut = 3,
}

/// <summary>A contribution proposal. Nothing becomes durable before native save and commit.</summary>
public enum ContributionResult
{
    /// <summary>A new expected identity is retained; some selected identities remain.</summary>
    Accepted = 1,

    /// <summary>The same identity and complete content are already retained; no new notice is staged.</summary>
    Duplicate = 2,

    /// <summary>This contribution stages technical completion and its notice.</summary>
    Completed = 3,

    /// <summary>Technical completion already closed the generation.</summary>
    AlreadyCompleted = 4,

    /// <summary>Timeout already closed the generation; this new content is not retained.</summary>
    AlreadyTimedOut = 5,
}

/// <summary>An explicit timeout proposal; a due signal alone never closes work.</summary>
public enum ContributionTimeoutResult
{
    /// <summary>Terminal timeout and its notice are staged.</summary>
    TimedOut = 1,

    /// <summary>The explicit observation precedes retained timeout eligibility.</summary>
    NotDue = 2,

    /// <summary>Technical completion already won.</summary>
    AlreadyCompleted = 3,

    /// <summary>The same retained generation already timed out.</summary>
    AlreadyTimedOut = 4,
}

/// <summary>The same retained contributor identity was reused with different complete content.</summary>
public sealed class ContributionConflictException : InvalidOperationException
{
    /// <summary>Creates a conflict without exposing retained payloads.</summary>
    public ContributionConflictException()
        : base("The retained contribution identity has different contract content.") { }
}
