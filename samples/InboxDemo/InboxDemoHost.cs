using System.Data;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.InboxDemo;

public static class InboxDemoHost
{
    public static WebApplication Build(
        string connection,
        string url,
        bool worker = false,
        string? operatorToken = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = typeof(InboxDemoHost).Assembly.FullName }
        );
        builder.WebHost.UseUrls(url);
        Register(builder.Services, connection);
        if (operatorToken is not null)
        {
            builder.Services.AddSingleton(new RenderOperatorCredential(operatorToken));
            builder
                .Services.AddAuthentication(RenderOperatorAuthenticationHandler.SchemeName)
                .AddScheme<
                    Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                    RenderOperatorAuthenticationHandler
                >(RenderOperatorAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization(options =>
                options.AddPolicy(
                    RenderOperatorAuthenticationHandler.Permission,
                    policy =>
                        policy
                            .AddAuthenticationSchemes(
                                RenderOperatorAuthenticationHandler.SchemeName
                            )
                            .RequireAuthenticatedUser()
                            .RequireClaim(
                                "permission",
                                RenderOperatorAuthenticationHandler.Permission
                            )
                )
            );
            builder.Services.AddPostgresInboxRecovery<RenderDbContext>();
        }

        if (worker)
            builder.Services.AddInboxWorker<RenderDbContext>(
                RenderExportHandler.Subscription,
                new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
            );
        var app = builder.Build();
        app.MapPost("/commands", ReceiveAsync);
        if (operatorToken is not null)
            RenderInboxOperations.Map(app);

        return app;
    }

    public static void Register(IServiceCollection services, string connection)
    {
        // Consumer supervision: a faulted background worker stops this sample host.
        services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost
        );
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

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        RenderDbContext database,
        IInbox<RenderDbContext> inbox,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (
                !Guid.TryParse(request.Headers["X-Message-Id"], out Guid id)
                || !int.TryParse(request.Headers["X-Message-Schema"], out int version)
            )
                return Results.BadRequest();
            var payload = await request.ReadFromJsonAsync<JsonElement>(cancellationToken);
            // Dedicated demo endpoint binding, not authentication of arbitrary headers.
            // Production admission/authentication belongs to the consumer before this construction.
            var message = new IncomingMessage(
                id,
                "exports",
                request.Headers["X-Message-Name"].ToString(),
                version,
                payload,
                Optional(request, "X-Tenant-Key"),
                Optional(request, "X-Correlation-Id"),
                Optional(request, "X-Causation-Id")
            );
            RenderExportHandler.Validate(message);
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
            await inbox.ReceiveAsync(RenderExportHandler.Subscription, message, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Accepted();
        }
        catch (InboxMessageConflictException)
        {
            return Results.Conflict();
        }
        catch (Exception failure)
            when (failure is ArgumentException or JsonException or InvalidDataException)
        {
            return Results.BadRequest();
        }
    }

    private static string? Optional(HttpRequest request, string header) =>
        request.Headers.TryGetValue(header, out var value) ? value.ToString() : null;
}
