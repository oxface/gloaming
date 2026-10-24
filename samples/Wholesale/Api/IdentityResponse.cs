using Gloaming.ActorIdentity;

namespace Gloaming.Samples.Wholesale.Api;

public sealed record IdentityResponse(ActorKind Kind, string? ActorId);
