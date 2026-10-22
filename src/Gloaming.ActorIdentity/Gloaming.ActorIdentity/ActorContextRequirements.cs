namespace Gloaming.ActorIdentity;

/// <summary>Presence and actor-kind checks; these checks do not supply authentication or permission policy.</summary>
public static class ActorContextRequirements
{
    /// <summary>Checks identity presence only; the consumer must establish trust and permission.</summary>
    public static Actor RequireIdentifiedActor(this ActorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Actor.Kind == ActorKind.Anonymous)
        {
            throw new IdentifiedActorRequiredException();
        }

        return context.Actor;
    }
}
