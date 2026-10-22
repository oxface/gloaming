namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Technical header metadata. Ownership and domain fields remain consumer-owned.</summary>
public interface IEventStreamRecord
{
    /// <summary>The stream identity within its consumer-selected discriminator.</summary>
    Guid Id { get; set; }

    /// <summary>Stable consumer-selected stream discriminator.</summary>
    string StreamType { get; set; }

    /// <summary>The stream or inline-state version represented by this record.</summary>
    long Version { get; set; }

    /// <summary>Changes on append and rebuild, including repair at the same event version.</summary>
    Guid ConcurrencyStamp { get; set; }

    /// <summary>UTC recorded time of stream creation.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC recorded time of the latest captured stream update.</summary>
    DateTimeOffset UpdatedAt { get; set; }
}
