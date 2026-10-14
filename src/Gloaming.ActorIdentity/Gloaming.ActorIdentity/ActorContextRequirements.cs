namespace Gloaming.ActorIdentity;

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
