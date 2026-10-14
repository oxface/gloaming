using Gloaming.Tenancy.AspNetCore;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;

// Host-owned selection presets explicitly bind the Organization admission resolver.
public static class OrganizationTenancyExtensions
{
    public static IServiceCollection AddOrganizationTenancyFromRoute(
        this IServiceCollection services,
        string routeValueName
    )
    {
        return services.AddRouteTenancy<OrganizationTenantResolver>(routeValueName);
    }

    public static IServiceCollection AddOrganizationTenancyFromSubdomain(
        this IServiceCollection services,
        string baseDomain
    )
    {
        return services.AddSubdomainTenancy<OrganizationTenantResolver>(baseDomain);
    }
}
