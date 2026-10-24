using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Access.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.Samples.Wholesale.Access;

public static class AccessRegistration
{
    public static IServiceCollection AddAccessQueries(this IServiceCollection services) =>
        services.AddScoped<IApplicationAccess, ApplicationAccess>();

    public static IServiceCollection AddMembershipAdministration(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAudit<AccessDbContext>, EfAudit<AccessDbContext>>();
        services.AddScoped<MembershipAudit>();
        return services.AddScoped<IMembershipAdministration, MembershipAdministration>();
    }
}
