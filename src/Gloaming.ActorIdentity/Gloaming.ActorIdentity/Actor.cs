namespace Gloaming.ActorIdentity;

/// <summary>Execution identity or explicit anonymity; it conveys no permission.</summary>
public sealed record Actor
{
    private Actor(ActorKind kind, ActorId? id)
    {
        Kind = kind;
        Id = id;
    }

    /// <summary>Whether this actor is anonymous, a human application identity or a system identity.</summary>
    public ActorKind Kind { get; }

    /// <summary>Opaque canonical identity for human/system actors; null for the anonymous actor.</summary>
    public ActorId? Id { get; }

    /// <summary>The explicitly anonymous actor; this is distinct from missing operation context.</summary>
    public static Actor Anonymous { get; } = new(ActorKind.Anonymous, null);

    /// <summary>Creates a human actor from an already authenticated and mapped application identity.</summary>
    public static Actor Human(ActorId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Actor(ActorKind.Human, id);
    }

    /// <summary>Creates a named system actor; construction does not authorize its work.</summary>
    public static Actor System(ActorId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Actor(ActorKind.System, id);
    }
}
