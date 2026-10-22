using Gloaming.ActorIdentity;

namespace ModulithFoundry.Samples.Wholesale.Api;

public sealed record IdentityResponse(ActorKind Kind, string? ActorId);
