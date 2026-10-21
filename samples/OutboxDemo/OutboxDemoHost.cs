using System.Diagnostics.Metrics;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ModulithFoundry.Samples.OutboxDemo;

public static class OutboxDemoHost
{
    public static WebApplication Build(
        string connection,
        string url,
        Uri? receiver = null,
        bool worker = false,
        string? operatorToken = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? observationInterval = null
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
        builder.Services.AddSingleton(provider => new ExportOutboxHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider,
            provider.GetRequiredService<IHostApplicationLifetime>(),
            worker
        ));
        builder
            .Services.AddHealthChecks()
            .AddCheck<ExportOutboxHealthCheck>("exports-outbox", timeout: TimeSpan.FromSeconds(3));
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<IHostedService>(provider => new ExportOutboxMetrics(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IMeterFactory>(),
            provider.GetRequiredService<ILogger<ExportOutboxMetrics>>(),
            observationInterval ?? TimeSpan.FromSeconds(5)
        ));
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
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready").AllowAnonymous();
        if (operatorToken is not null)
            ExportOutboxOperations.Map(app);

        return app;
    }
}
