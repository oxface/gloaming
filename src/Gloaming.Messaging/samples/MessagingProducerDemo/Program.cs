using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Messaging.ServiceDefaults;
using ModulithFoundry.Samples.OutboxDemo;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(
            builder.Configuration["OTEL_SERVICE_NAME"] ?? "messaging-producer",
            serviceInstanceId: Guid.NewGuid().ToString()
        )
    )
    .WithTracing(tracing => tracing.AddGloamingMessaging())
    .WithMetrics(metrics => metrics.AddGloamingMessaging());

string connection =
    builder.Configuration.GetConnectionString("Exports")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Exports.");
builder.Services.AddDbContext<ExportDbContext>(options =>
    ExportDbContext.Configure(options, connection)
);
builder.Services.AddPostgresOutbox<ExportDbContext>();
builder.Services.AddScoped<ExportRequestCommands>();

var app = builder.Build();
app.MapGet("/health/live", () => Results.Ok());
app.MapPost(
    "/exports",
    async (Draft draft, ExportDbContext database, CancellationToken cancellation) =>
    {
        if (draft.Id == Guid.Empty || draft.Pages < 0)
            return Results.BadRequest();

        await using var transaction = await database.Database.BeginTransactionAsync(cancellation);
        database.Add(ExportRequest.Create(draft.Id, draft.Pages));
        await database.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return Results.Created($"/exports/{draft.Id}", new { draft.Id, Version = 1 });
    }
);
app.MapPost(
    "/exports/{id:guid}/submit",
    async (
        Guid id,
        Submission submission,
        ExportDbContext database,
        ExportRequestCommands commands,
        CancellationToken cancellation
    ) =>
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellation);
        var result = await commands.SubmitAsync(id, submission.ExpectedVersion, cancellation);
        if (result != ExportSubmissionResult.Accepted)
            return result switch
            {
                ExportSubmissionResult.NotFound => Results.NotFound(),
                ExportSubmissionResult.Conflict => Results.Conflict(),
                _ => Results.UnprocessableEntity(),
            };

        await database.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return Results.Accepted($"/exports/{id}");
    }
);
await app.RunAsync();

internal sealed record Draft(Guid Id, int Pages);

internal sealed record Submission(long ExpectedVersion);
