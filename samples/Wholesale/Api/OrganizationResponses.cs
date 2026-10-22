using Gloaming.ActorIdentity;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Api;

public sealed record TenantIdentityResponse(ActorKind Kind, string? ActorId, string TenantId);

public sealed record CatalogResponse(TenantIdentityResponse Context, StockAvailability Stock);
