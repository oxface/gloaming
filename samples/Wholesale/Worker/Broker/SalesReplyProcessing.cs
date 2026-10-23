using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Worker.Broker;

// A finite consumer admission policy, not tenant discovery or a global message bootstrap protocol.
public static class SalesReplyProcessing
{
    public static IServiceCollection AddSalesReservationReplyProcessing(
        this IServiceCollection services,
        string runtimeConnection,
        string organization
    )
    {
        if (organization is not ("wholesale-alpha" or "wholesale-beta"))
            throw new InvalidOperationException(
                "Admit wholesale-alpha or wholesale-beta before Sales reply processing."
            );

        services.AddScoped(_ =>
        {
            var tenant = new TenantContextAccessor();
            tenant.Initialize(TenantContext.ForTenant(new TenantId(organization)));
            return tenant;
        });
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped(_ =>
        {
            var actor = new ActorContextAccessor();
            actor.Initialize(
                new ActorContext(
                    Actor.System(new ActorId(StockReservationReplyAdmission.WorkerActor))
                )
            );
            return actor;
        });
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddSalesReservationReplyDatabase(runtimeConnection);
        return services.AddAdmittedStockReservationReplies();
    }
}
