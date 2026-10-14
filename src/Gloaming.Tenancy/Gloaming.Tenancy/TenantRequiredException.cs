namespace Gloaming.Tenancy;

/// <summary>A declared context requirement failed; consumers own failure presentation.</summary>
public sealed class TenantRequiredException : InvalidOperationException
{
    internal TenantRequiredException()
        : base("The operation requires a selected tenant.") { }
}
