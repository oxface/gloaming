namespace Gloaming.ActorIdentity;

/// <summary>Read access to the context established for the owning operation scope.</summary>
public interface IActorContextAccessor
{
    /// <exception cref="InvalidOperationException">Context has not been established.</exception>
    /// <exception cref="ObjectDisposedException">The owning accessor has been disposed.</exception>
    ActorContext Current { get; }
}
