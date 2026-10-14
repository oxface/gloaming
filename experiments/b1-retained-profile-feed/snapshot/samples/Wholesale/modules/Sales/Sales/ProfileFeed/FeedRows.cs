using System.Text.Json;

namespace ModulithFoundry.Samples.Wholesale.Sales.ProfileFeed;

internal sealed class FeedStateRow
{
    public required string OrganizationKey { get; set; }
    public Guid Generation { get; set; }
    public long Head { get; set; }
    public long Floor { get; set; }
}

internal sealed class ProfileChangeRow
{
    public required string OrganizationKey { get; set; }
    public long Position { get; set; }
    public Guid ChangeId { get; set; }
    public long ProfileVersion { get; set; }
    public JsonElement Payload { get; set; }
}
