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
    /// <summary>Exposes durable in-process outcome receipt; compose with reservation workflow registration.</summary>
    public static IServiceCollection AddStockReservationOutcomeIntake(
        this IServiceCollection services
    ) =>
        services.AddSingleton<
            IStockReservationOutcomeIntake,
            ReservationOrders.StockReservationOutcomeIntake
        >();

    public static IServiceCollection AddCustomerOrderAllowances(this IServiceCollection services) =>
        services.AddScoped<ICustomerOrderAllowances, OrderAllowances.CustomerOrderAllowances>();

    public static IServiceCollection AddDraftOrderCommands(this IServiceCollection services)
    {
        services.AddDraftOrderAuthorization();
        return services.AddScoped<IDraftOrderCommands, DraftOrders.DraftOrderCommands>();
    }

    public static IServiceCollection AddDraftOrderQueries(this IServiceCollection services)
    {
        services.AddDraftOrderAuthorization();
        return services.AddScoped<IDraftOrderQueries, DraftOrders.DraftOrderQueries>();
    }

    public static IServiceCollection AddDraftOrderAuthorization(this IServiceCollection services)
    {
        services.TryAddScoped<DraftOrders.DraftOrderAuthorization>();
        services.TryAddScoped<IDraftOrderAuthorization>(provider =>
            provider.GetRequiredService<DraftOrders.DraftOrderAuthorization>()
        );
        return services;
    }

    /// <summary>Registers the compensating two-line process, leaving transport and worker hosting explicit.</summary>
    public static IServiceCollection AddStockReservationOrders(this IServiceCollection services)
    {
        RegisterReservationWorkflow(services);
        services.AddInboxHandler<SalesDbContext, ReservationOrders.StockReservationReplyHandler>(
            StockReservationReplyAdmission.Subscription
        );
        return services.AddScoped<
            IStockReservationOrders,
            ReservationOrders.StockReservationOrders
        >();
    }

    /// <summary>Registers reply processing for a host-admitted immutable tenant/system-actor scope.</summary>
    public static IServiceCollection AddAdmittedStockReservationReplies(
        this IServiceCollection services
    )
    {
        RegisterReservationWorkflow(services);
        services.AddScoped<ReservationOrders.StockReservationReplyHandler>();
        return services.AddInboxHandler<
            SalesDbContext,
            ReservationOrders.AdmittedStockReservationReplyHandler
        >(StockReservationReplyAdmission.Subscription);
    }

    private static void RegisterReservationWorkflow(IServiceCollection services)
    {
        RegisterMessaging(services);
        services.AddWorkflow<
            SalesDbContext,
            StartStockReservationOrder,
            ReservationOrders.ReservationState
        >(_ => ReservationOrders.ReservationWorkflow.Define());
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
