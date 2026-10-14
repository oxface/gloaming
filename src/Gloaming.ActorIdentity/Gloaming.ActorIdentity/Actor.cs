namespace Gloaming.ActorIdentity;

/// <summary>Execution identity or explicit anonymity; it conveys no permission.</summary>
public sealed record Actor
{
    private Actor(ActorKind kind, ActorId? id)
    {
        Kind = kind;
        Id = id;
    }

    public ActorKind Kind { get; }

    public ActorId? Id { get; }

    public static Actor Anonymous { get; } = new(ActorKind.Anonymous, null);

    public static Actor Human(ActorId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Actor(ActorKind.Human, id);
    }

    public static Actor System(ActorId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Actor(ActorKind.System, id);
    }
}
