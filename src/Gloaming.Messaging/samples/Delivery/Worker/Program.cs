using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Messaging.Exports;
using Gloaming.Samples.Messaging.Rendering;
using Gloaming.Samples.Messaging.ServiceDefaults;
using Gloaming.Samples.Messaging.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);
string role =
    builder.Configuration["role"]
    ?? throw new InvalidOperationException("Choose --role setup, dispatch, receive or process.");
using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
if (role == "setup")
{
    await WorkerSetup.RunAsync(builder.Configuration, startup.Token);
    return;
}

builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost
);
builder.AddServiceDefaults();
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(
            builder.Configuration["OTEL_SERVICE_NAME"] ?? "messaging-" + role,
            serviceInstanceId: Guid.NewGuid().ToString()
        )
    )
    .WithTracing(tracing =>
        tracing
            .AddSource("Gloaming.Messaging")
            .AddSource("RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber")
    )
    .WithMetrics(metrics => metrics.AddMeter("Gloaming.Messaging"));

// Each long-lived transport belongs to this process. Scoped dispatch/intake operations
// reuse its channel sequentially; they do not create a broker connection per message.
await using var broker = role is "dispatch" or "receive"
    ? await BrokerSession.OpenAsync(builder.Configuration, createQueue: false, startup.Token)
    : null;
var polling = new OutboxWorkerOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));
switch (role)
{
    case "dispatch":
    {
        string connection = WorkerSetup.Connection(builder.Configuration, "Exports");
        await using (var database = ExportDbContext.Create(connection))
            _ = await database.Set<OutboxMessageRecord>().AnyAsync(startup.Token);

        builder.Services.AddDbContext<ExportDbContext>(options =>
            ExportDbContext.Configure(options, connection)
        );
        builder.Services.AddScoped(provider => new WorkerPublisher(
            broker!,
            provider.GetRequiredService<ILogger<WorkerPublisher>>()
        ));
        builder.Services.AddPostgresOutboxDispatcher<ExportDbContext, WorkerPublisher>(
            new(
                TimeSpan.FromSeconds(builder.Configuration.GetValue("Worker:LeaseSeconds", 30)),
                TimeSpan.FromSeconds(1)
            )
        );
        builder.Services.AddOutboxWorker<ExportDbContext>(polling);
        break;
    }
    case "receive":
    {
        string connection = WorkerSetup.Connection(builder.Configuration, "Rendering");
        await WorkerSetup.RequireRenderingAsync(connection, startup.Token);
        builder.Services.AddDbContext<RenderDbContext>(options =>
            RenderDbContext.Configure(options, connection)
        );
        builder.Services.AddPostgresInbox<RenderDbContext>();
        builder.Services.AddSingleton(broker!);
        builder.Services.AddHostedService<RabbitMqIntakeWorker>();
        break;
    }
    case "process":
    {
        string connection = WorkerSetup.Connection(builder.Configuration, "Rendering");
        await WorkerSetup.RequireRenderingAsync(connection, startup.Token);
        RenderServices.AddRendering(builder.Services, connection);
        builder.Services.AddInboxWorker<RenderDbContext>(
            RenderExportHandler.Subscription,
            new(polling.IdleDelay, polling.FailureDelay)
        );
        break;
    }
    default:
        throw new InvalidOperationException("Choose --role setup, dispatch, receive or process.");
}

using var host = builder.Build();
using var brokerObservation = broker?.Supervise(
    host.Services.GetRequiredService<IHostApplicationLifetime>(),
    host.Services.GetRequiredService<ILogger<BrokerSession>>()
);
await host.RunAsync();
