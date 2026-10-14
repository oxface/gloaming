namespace Gloaming.Tenancy;

/// <summary>Read access to the context established for the owning operation scope.</summary>
public interface ITenantContextAccessor
{
    /// <exception cref="InvalidOperationException">Context has not been established.</exception>
    /// <exception cref="ObjectDisposedException">The owning accessor has been disposed.</exception>
    TenantContext Current { get; }
}
