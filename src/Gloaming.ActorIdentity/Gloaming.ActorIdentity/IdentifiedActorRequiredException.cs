namespace Gloaming.ActorIdentity;

/// <summary>A declared context requirement failed; consumers own failure presentation.</summary>
public sealed class IdentifiedActorRequiredException : InvalidOperationException
{
    internal IdentifiedActorRequiredException()
        : base("The operation requires an identified actor.") { }
}
