namespace Gloaming.ActorIdentity;

/// <summary>The immutable executing actor and optional origin attribution for an operation.</summary>
public sealed record ActorContext
{
    /// <summary>Captures the executing actor and optional trusted initiator for one immutable operation.</summary>
    public ActorContext(Actor actor, Actor? initiator = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Actor = actor;
        Initiator = initiator;
    }

    /// <summary>The identity actually executing this operation, independently of its initiator.</summary>
    public Actor Actor { get; }

    /// <summary>Optional attribution supplied by the consumer; it grants no authority.</summary>
    public Actor? Initiator { get; }
}
