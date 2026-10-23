using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.EventReactions;

public static class DirectOrdersRegistration
{
    public static IServiceCollection AddDirectOrdersModule(
        this IServiceCollection services,
        string connection,
        Action<
            EventReactionRegistration<DirectOrdersContext, DirectOrdersModule>
        >? configureEvents = null
    )
    {
        services.AddEventReactions<DirectOrdersContext, DirectOrdersModule>(events =>
        {
            events.CollectFrom<DirectOrder>(order => order.Events);
            events.CollectFrom<DirectAllowance>(allowance => allowance.Events);
            events.AddReaction<OrderSubmitted, ReserveOrderAllowance>();
            events.AddReaction<OrderSubmitted, RecordOrderReceipt>();
            events.AddReaction<AllowanceAllocated, RecordOrderAllocation>();
            events.AddReaction<OrderDeleted, ReleaseOrderAllowance>();
            configureEvents?.Invoke(events);
        });
        return services.AddDbContext<DirectOrdersContext>(
            (provider, options) =>
                options
                    .ConfigureGloamingCapabilities<DirectOrdersContext>(provider)
                    .UseNpgsql(
                        connection,
                        postgres =>
                            postgres.MigrationsHistoryTable(
                                "__EFMigrationsHistory",
                                "direct_orders"
                            )
                    )
        );
    }
}
