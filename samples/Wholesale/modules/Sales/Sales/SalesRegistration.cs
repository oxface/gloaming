using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales;

public static class SalesRegistration
{
    public static IServiceCollection AddDraftOrderCommands(this IServiceCollection services) =>
        services.AddScoped<IDraftOrderCommands, DraftOrders.DraftOrderCommands>();

    public static IServiceCollection AddDraftOrderQueries(this IServiceCollection services) =>
        services.AddScoped<IDraftOrderQueries, DraftOrders.DraftOrderQueries>();

    /// <summary>Registers the compensating two-line process, leaving transport and worker hosting explicit.</summary>
    public static IServiceCollection AddStockReservationOrders(this IServiceCollection services)
    {
        RegisterMessaging(services);
        services.AddWorkflow<
            SalesDbContext,
            StartStockReservationOrder,
            ReservationOrders.ReservationState
        >(_ => ReservationOrders.ReservationWorkflow.Define());
        services.AddInboxHandler<SalesDbContext, ReservationOrders.StockReservationReplyHandler>(
            StockReservationReplyAdmission.Subscription
        );
        return services.AddScoped<
            IStockReservationOrders,
            ReservationOrders.StockReservationOrders
        >();
    }

    /// <summary>Registers Sales requests and reply processing without starting workers or configuring transport.</summary>
    public static IServiceCollection AddStockIssueRequests(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        RegisterMessaging(services);
        services.AddInboxHandler<SalesDbContext, StockIssues.StockIssueReplyHandler>(
            StockIssueReplyAdmission.Subscription
        );
        services.AddScoped<StockIssueDeadlines>();
        return services.AddScoped<IStockIssueRequests, StockIssues.StockIssueRequests>();
    }

    private static void RegisterMessaging(IServiceCollection services)
    {
        services.TryAddScoped<ActorContextAccessor>();
        services.TryAddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.TryAddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddPostgresOutbox<SalesDbContext>();
        services.AddPostgresInbox<SalesDbContext>();
        services.AddPostgresInboxProcessor<SalesDbContext>(new(TimeSpan.FromSeconds(1)));
    }

    public static IServiceCollection AddCustomerProfiles(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAudit<SalesDbContext>, EfAudit<SalesDbContext>>();
        services.AddScoped<SalesAudit>();
        return services.AddScoped<ICustomerProfiles, CustomerProfiles>();
    }
}
