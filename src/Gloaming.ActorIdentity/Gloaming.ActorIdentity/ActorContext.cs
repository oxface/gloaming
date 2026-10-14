namespace Gloaming.ActorIdentity;

/// <summary>The immutable executing actor and optional origin attribution for an operation.</summary>
public sealed record ActorContext
{
    public ActorContext(Actor actor, Actor? initiator = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Actor = actor;
        Initiator = initiator;
    }

    public Actor Actor { get; }

    /// <summary>Optional attribution supplied by the consumer; it grants no authority.</summary>
    public Actor? Initiator { get; }
}
