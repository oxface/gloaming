using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Gloaming.Samples.Wholesale.Api;

public static class RuntimeHealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapHealthChecks("/health/ready").AllowAnonymous().AllowTenantless();
        app.MapHealthChecks(
                "/health/live",
                new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains("live"),
                }
            )
            .AllowAnonymous()
            .AllowTenantless();
    }
}
