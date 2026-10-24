using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Read-only retained expected-set, original terminal envelopes and native generation progress.</summary>
public sealed class ContributionSetRecord
{
    private ContributionSetRecord() { }

    /// <summary>Ordinal complete technical scope.</summary>
    public string ScopeKey { get; private set; } = null!;

    /// <summary>Consumer work identity.</summary>
    public Guid WorkId { get; private set; }

    /// <summary>Immutable retained generation.</summary>
    public Guid GenerationId { get; private set; }

    /// <summary>Versioned immutable explicit expected identities/count, retained as JSONB values.</summary>
    public string Expected { get; private set; } = null!;

    /// <summary>Versioned original completion envelope; payload is opaque to technical completion.</summary>
    public string Completion { get; private set; } = null!;

    /// <summary>Optional explicit UTC microsecond deadline eligibility.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>Versioned original finite signal envelope, or null when no timeout was selected.</summary>
    public string? TimeoutSignal { get; private set; }

    /// <summary>Versioned original timeout outcome envelope, or null when no timeout was selected.</summary>
    public string? TimeoutOutcome { get; private set; }

    /// <summary>Monotonic native revision concurrency token.</summary>
    public long Revision { get; private set; }

    /// <summary>Retained technical lifecycle.</summary>
    public ContributionSetState State { get; private set; }

    internal static ContributionSetRecord Create(
        ContributionSetKey key,
        ContributionSetDefinition definition
    ) =>
        new()
        {
            ScopeKey = key.ScopeKey,
            WorkId = key.WorkId,
            GenerationId = key.GenerationId,
            Expected = JsonSerializer.Serialize(
                new ExpectedSet(1, definition.Expected.ToArray(), definition.Required)
            ),
            Completion = ContributionEnvelope.Write(definition.Completion),
            DueAt = definition.Timeout?.DueAt,
            TimeoutSignal = definition.Timeout is null
                ? null
                : ContributionEnvelope.Write(definition.Timeout.Signal),
            TimeoutOutcome = definition.Timeout is null
                ? null
                : ContributionEnvelope.Write(definition.Timeout.Outcome),
            Revision = 1,
            State = ContributionSetState.Open,
        };

    internal ContributionSetDefinition Definition()
    {
        _ = new ContributionSetKey(ScopeKey, WorkId, GenerationId);
        var expected = JsonSerializer.Deserialize<ExpectedSet>(Expected);
        if (
            expected?.Protocol != 1
            || expected.Identities is null
            || Revision < 1
            || !Enum.IsDefined(State)
            || DueAt is null && (TimeoutSignal is not null || TimeoutOutcome is not null)
            || DueAt is not null && (TimeoutSignal is null || TimeoutOutcome is null)
        )
            throw new InvalidDataException(
                "Unsupported or invalid retained contribution-set definition."
            );

        return new(
            expected.Identities,
            expected.Required,
            ContributionEnvelope.Read(Completion),
            DueAt is { } due
                ? new(
                    due,
                    ContributionEnvelope.Read(TimeoutSignal!),
                    ContributionEnvelope.Read(TimeoutOutcome!)
                )
                : null
        );
    }

    internal void Advance(ContributionSetState state)
    {
        State = state;
        Revision = checked(Revision + 1);
    }

    private sealed record ExpectedSet(int Protocol, string[] Identities, int Required);
}

/// <summary>Read-only immutable named contribution content retained for the complete set generation.</summary>
public sealed class ContributionRecord
{
    private ContributionRecord() { }

    /// <summary>Ordinal technical scope.</summary>
    public string ScopeKey { get; private set; } = null!;

    /// <summary>Consumer work identity.</summary>
    public Guid WorkId { get; private set; }

    /// <summary>Immutable generation.</summary>
    public Guid GenerationId { get; private set; }

    /// <summary>One of the exact expected ordinal identities; independent of delivery identity.</summary>
    public string ContributorId { get; private set; } = null!;

    /// <summary>Original stable content contract name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Original positive content schema version.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Owned immutable opaque JSON content.</summary>
    public JsonElement Payload { get; private set; }

    internal static ContributionRecord Create(
        ContributionSetKey key,
        string contributor,
        ContributionValue value
    ) =>
        new()
        {
            ScopeKey = key.ScopeKey,
            WorkId = key.WorkId,
            GenerationId = key.GenerationId,
            ContributorId = contributor,
            Name = value.Name,
            SchemaVersion = value.SchemaVersion,
            Payload = value.Payload.Clone(),
        };

    internal ContributionValue Value() => new(Name, SchemaVersion, Payload);
}

internal sealed record ContributionEnvelope(int Protocol, OutgoingMessage Message)
{
    internal static string Write(OutgoingMessage message) =>
        JsonSerializer.Serialize(new ContributionEnvelope(1, message));

    internal static OutgoingMessage Read(string text)
    {
        var envelope = JsonSerializer.Deserialize<ContributionEnvelope>(text);
        if (envelope?.Protocol != 1 || envelope.Message is null)
            throw new InvalidDataException("Unsupported retained contribution outcome envelope.");

        return envelope.Message;
    }
}
