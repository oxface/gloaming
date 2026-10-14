namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Technical metadata of one inline row per stream; state shape remains consumer-owned.</summary>
public interface IInlineStateRecord
{
    Guid StreamId { get; set; }
    long Version { get; set; }
    DateTimeOffset RecordedAt { get; set; }
}
