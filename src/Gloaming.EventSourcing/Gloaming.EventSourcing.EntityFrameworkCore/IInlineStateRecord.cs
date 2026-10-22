namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Technical metadata of one inline row per stream; state shape remains consumer-owned.</summary>
public interface IInlineStateRecord
{
    /// <summary>The owning stream identity.</summary>
    Guid StreamId { get; set; }

    /// <summary>The stream or inline-state version represented by this record.</summary>
    long Version { get; set; }

    /// <summary>UTC application append time, distinct from commit time.</summary>
    DateTimeOffset RecordedAt { get; set; }
}
