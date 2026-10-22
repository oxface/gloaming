using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.Samples.Messaging.Rendering;

public static class RenderServices
{
    public static void AddRendering(IServiceCollection services, string connection)
    {
        services.AddDbContext<RenderDbContext>(options =>
            RenderDbContext.Configure(options, connection)
        );
        services.AddPostgresInbox<RenderDbContext>();
        services.AddPostgresInboxProcessor<RenderDbContext>(new(TimeSpan.FromSeconds(1)));
        services.AddInboxHandler<RenderDbContext, RenderExportHandler>(
            RenderExportHandler.Subscription
        );
        services.AddInboxFailurePolicy<RenderDbContext, RenderInboxFailurePolicy>(
            RenderExportHandler.Subscription
        );
    }
}
