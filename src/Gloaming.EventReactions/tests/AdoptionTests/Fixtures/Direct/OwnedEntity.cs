namespace Gloaming.Samples.EventReactions;

// Consumer-owned inheritance is independent of event collection.
public abstract class OwnedEntity
{
    public string Owner { get; protected set; } = "";
}
