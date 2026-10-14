namespace Gloaming.ActorIdentity.AspNetCore;

/// <summary>An authenticated principal could not be mapped to an identified application actor.</summary>
public sealed class HttpActorResolutionException(string message) : Exception(message);
