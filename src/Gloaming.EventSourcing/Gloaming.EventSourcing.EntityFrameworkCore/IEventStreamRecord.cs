namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Technical header metadata. Ownership and domain fields remain consumer-owned.</summary>
public interface IEventStreamRecord
{
    Guid Id { get; set; }
    string StreamType { get; set; }
    long Version { get; set; }

    /// <summary>Changes on append and rebuild, including repair at the same event version.</summary>
    Guid ConcurrencyStamp { get; set; }
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
