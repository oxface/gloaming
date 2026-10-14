namespace Gloaming.EventSourcing.EntityFrameworkCore;

internal static class EventStreamValidation
{
    internal static void ValidateExistingStream(IEventStreamRecord stream, string streamType)
    {
        if (
            stream.StreamType != streamType
            || stream.Version < 1
            || stream.ConcurrencyStamp == Guid.Empty
            || stream.CreatedAt.Offset != TimeSpan.Zero
            || stream.UpdatedAt.Offset != TimeSpan.Zero
            || stream.CreatedAt > stream.UpdatedAt
        )
            throw new InvalidDataException(
                "The observed stream type/version/timestamps are invalid."
            );
    }
}
