using System.Text.Json.Serialization;

namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

public sealed record CustomerProfileFeedCursor(Guid Generation, long AfterPosition);

public sealed record CustomerProfileFeedBounds(
    CustomerProfileFeedCursor EarliestAvailable,
    CustomerProfileFeedCursor Head
);

/// <summary>Sales public change schema V1, independent of private rows and domain events.</summary>
public sealed record CustomerProfileChangedV1(
    [property: JsonRequired] Guid CustomerId,
    [property: JsonRequired] Guid AddressId,
    [property: JsonRequired] string Code,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] string AddressLine,
    [property: JsonRequired] long Version
);

public sealed record CustomerProfileFeedChange(
    long Position,
    Guid ChangeId,
    CustomerProfileChangedV1 Profile
);

public abstract record CustomerProfileFeedRead
{
    /// <summary>Checkpoint Next, not Bounds.Head when HasMore is true.</summary>
    public sealed record Page(
        CustomerProfileFeedBounds Bounds,
        IReadOnlyList<CustomerProfileFeedChange> Changes,
        CustomerProfileFeedCursor Next,
        bool HasMore
    ) : CustomerProfileFeedRead;

    /// <summary>Lost retained history; the subscriber must stop and reconcile explicitly.</summary>
    public sealed record CursorExpired(CustomerProfileFeedBounds Bounds) : CustomerProfileFeedRead;

    /// <summary>Wrong source generation or a position beyond the captured committed head.</summary>
    public sealed record InvalidCursor : CustomerProfileFeedRead;
}
