using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ModulithFoundry.Samples.OutboxDemo;

public static class OutboxDemoHost
{
    public static WebApplication Build(
        string connection,
        string url,
        Uri? receiver = null,
        bool worker = false,
        string? operatorToken = null,
        TimeSpan? leaseDuration = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = typeof(OutboxDemoHost).Assembly.FullName }
        );
        builder.WebHost.UseUrls(url);
        builder.Services.AddDbContext<ExportDbContext>(options =>
            ExportDbContext.Configure(options, connection)
        );
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost
        );
        if (worker)
        {
            ArgumentNullException.ThrowIfNull(receiver);

            builder.Services.AddSingleton(_ => new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
            });
            builder.Services.AddScoped(provider => new HttpCommandPublisher(
                provider.GetRequiredService<HttpClient>(),
                receiver
            ));
            builder.Services.AddPostgresOutboxDispatcher<ExportDbContext, HttpCommandPublisher>(
                new(leaseDuration ?? TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5))
            );
            builder.Services.AddOutboxFailurePolicy<ExportDbContext, ExportOutboxFailurePolicy>();
            builder.Services.AddOutboxWorker<ExportDbContext>(
                new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
            );
        }

        if (operatorToken is not null)
        {
            builder.Services.AddSingleton(new ExportOperatorCredential(operatorToken));
            builder
                .Services.AddAuthentication(ExportOperatorAuthenticationHandler.SchemeName)
                .AddScheme<
                    Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                    ExportOperatorAuthenticationHandler
                >(ExportOperatorAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization(options =>
                options.AddPolicy(
                    ExportOperatorAuthenticationHandler.Permission,
                    policy =>
                        policy
                            .AddAuthenticationSchemes(
                                ExportOperatorAuthenticationHandler.SchemeName
                            )
                            .RequireAuthenticatedUser()
                            .RequireClaim(
                                "permission",
                                ExportOperatorAuthenticationHandler.Permission
                            )
                )
            );
            builder.Services.AddPostgresOutboxDeadLetters<ExportDbContext>();
        }

        var app = builder.Build();
        if (operatorToken is not null)
            ExportOutboxOperations.Map(app);

        return app;
    }
}
