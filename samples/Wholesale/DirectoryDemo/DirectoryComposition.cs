using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.DirectoryDemo;

public static class DirectoryComposition
{
    public static IServiceCollection AddDeliveryDirectory(
        this IServiceCollection services,
        string sales,
        string dispatch
    )
    {
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddSalesDatabase(sales);
        services.AddCustomerProfiles();
        services.AddDispatchDirectory(dispatch);
        return services.AddPostgresOutboxDispatcher<SalesDbContext, DirectoryPublisher>(
            new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50))
        );
    }

    public static IServiceCollection AddDirectoryWorkers(this IServiceCollection services)
    {
        services.AddOutboxWorker<SalesDbContext>(
            new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
        );
        return services.AddInboxWorker<DispatchDbContext>(
            DispatchRegistration.DirectorySubscription,
            new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
        );
    }

    public static void Establish(IServiceProvider services, string organization)
    {
        services
            .GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        services
            .GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.Human(new ActorId("directory-operator"))));
    }

    public static async Task<bool> CopyNextAsync(
        IServiceProvider services,
        string organization,
        CancellationToken token
    )
    {
        await using var scope = services.CreateAsyncScope();
        Establish(scope.ServiceProvider, organization);
        var directory = scope.ServiceProvider.GetRequiredService<ICustomerDirectory>();
        var progress = await directory.BeginAsync(token);
        if (progress.Ready)
            return true;

        var page = await scope
            .ServiceProvider.GetRequiredService<ICustomerDeliveryDirectory>()
            .ReadPageAsync(progress.After, 100, token);
        // No source I/O occurs under a receiver database lock or transaction.
        await directory.ApplyPageAsync(progress.PopulationId, progress.After, page, token);
        return page.Complete;
    }
}

// The relay knows only Dispatch's public boundary and Sales's public replacement contract.
public sealed class DirectoryPublisher(ICustomerDirectoryIntake intake) : IMessagePublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (
            message.RouteKey != DispatchRegistration.DirectorySubscription
            || message.MessageName != "sales.customer-delivery-state"
            || message.SchemaVersion != 1
            || message.TenantKey is null
        )
            throw new InvalidDataException("Unsupported directory route or envelope.");

        var state =
            message.Payload.Deserialize<CustomerDeliveryState>(Json)
            ?? throw new InvalidDataException("Missing directory state.");
        return intake.ReceiveAsync(message.MessageId, message.TenantKey, state, cancellationToken);
    }
}
