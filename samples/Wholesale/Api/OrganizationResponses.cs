using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Inventory.Contracts;

namespace Gloaming.Samples.Wholesale.Api;

public sealed record TenantIdentityResponse(ActorKind Kind, string? ActorId, string TenantId);

public sealed record CatalogResponse(TenantIdentityResponse Context, StockAvailability Stock);
