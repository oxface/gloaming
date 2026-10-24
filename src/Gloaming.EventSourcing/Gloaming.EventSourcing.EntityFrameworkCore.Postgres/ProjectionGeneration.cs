using Microsoft.EntityFrameworkCore;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

/// <summary>Consumer-owned reducer and retained compatibility identity for one generation.</summary>
public sealed class ProjectionGeneration<TContext>
    where TContext : DbContext
{
    /// <summary>Selects a fresh output generation, retained reducer identity and cooperative staging callback.</summary>
    public ProjectionGeneration(
        string name,
        string generation,
        string definitionId,
        Func<TContext, string, FeedPage, CancellationToken, Task> apply
    )
    {
        Identity(name);
        Identity(generation);
        Identity(definitionId);
        ArgumentNullException.ThrowIfNull(apply);

        Name = name;
        Generation = generation;
        DefinitionId = definitionId;
        Apply = apply;
    }

    /// <summary>Stable projection identity.</summary>
    public string Name { get; }

    /// <summary>Unique retained output/checkpoint generation, never reused.</summary>
    public string Generation { get; }

    /// <summary>Consumer-attested compatibility identity of this reducer and output format.</summary>
    public string DefinitionId { get; }
    internal Func<TContext, string, FeedPage, CancellationToken, Task> Apply { get; }

    internal static void Identity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || value.Contains('\0'))
            throw new ArgumentException(
                "Use a nonempty ordinal identity of at most256 characters without NUL."
            );
    }
}

/// <summary>Terminal result after both native transaction and scope cleanup attempts.</summary>
public enum ProjectionStatus
{
    /// <summary>Page writes and progress committed with acknowledgment.</summary>
    Applied = 1,

    /// <summary>No events remained through the observed head; progress committed.</summary>
    NoWork = 2,

    /// <summary>Another turn holds this generation's cooperative native lock; no callback ran.</summary>
    Busy = 3,

    /// <summary>Activation candidate still has retained events to consume.</summary>
    NotCaughtUp = 4,

    /// <summary>The current active pointer differs from the operator's expected value.</summary>
    Conflict = 5,

    /// <summary>Generation pointer commit was acknowledged.</summary>
    Activated = 6,

    /// <summary>The requested generation was already selected; no pointer write was needed.</summary>
    AlreadyActive = 7,

    /// <summary>A precommit failure stopped this operation; inspect Error.</summary>
    Failed = 8,

    /// <summary>Requested cancellation stopped this operation before commit began.</summary>
    Cancelled = 9,

    /// <summary>Commit began without acknowledgment; fresh retained state decides recovery.</summary>
    Indeterminate = 10,
}

/// <summary>Commit uncertainty requires a fresh retained-state read, never automatic reducer retry.</summary>
public sealed record ProjectionResult(
    ProjectionStatus Status,
    FeedPage? Page = null,
    Exception? Error = null
);
