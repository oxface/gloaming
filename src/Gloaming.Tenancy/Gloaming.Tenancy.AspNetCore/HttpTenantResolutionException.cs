namespace Gloaming.Tenancy.AspNetCore;

/// <summary>HTTP tenant selection/resolution failed; the consumer chooses failure presentation.</summary>
public sealed class HttpTenantResolutionException(string message) : Exception(message);
