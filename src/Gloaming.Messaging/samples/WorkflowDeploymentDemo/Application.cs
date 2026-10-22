using System.Text.Json;
using System.Text.Json.Serialization;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#if EARLIER_BUILD
namespace ModulithFoundry.Samples.WorkflowDeploymentDemo.Earlier;

#else
namespace ModulithFoundry.Samples.WorkflowDeploymentDemo.Current;

#endif

// Separate assemblies let the proof actually retire the earlier process before starting current code.
public static class Application
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string Subscription = "export-replies";

    public static async Task<int> Main(string[] args)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        try
        {
            var connection =
                Environment.GetEnvironmentVariable("WORKFLOW_DEPLOYMENT_CONNECTION")
                ?? throw new InvalidOperationException("Set WORKFLOW_DEPLOYMENT_CONNECTION.");
            var mode =
                Environment.GetEnvironmentVariable("WORKFLOW_DEPLOYMENT_MODE") ?? "compatible";
            var services = new ServiceCollection();
            services.AddDbContext<DeploymentDatabase>(options => options.UseNpgsql(connection));
            services.AddPostgresOutbox<DeploymentDatabase>();
            services.AddPostgresInbox<DeploymentDatabase>();
            services.AddPostgresInboxProcessor<DeploymentDatabase>(
                new(TimeSpan.FromMilliseconds(1))
            );
            services.AddWorkflow<DeploymentDatabase, ExportInput, ExportState>(_ => Define(mode));
            services.AddInboxHandler<DeploymentDatabase, ExportReplies>(Subscription);
            await using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<DeploymentDatabase>();
            var workflow = scope.ServiceProvider.GetRequiredService<
                IWorkflow<ExportInput, ExportState>
            >();
#if EARLIER_BUILD
            Console.WriteLine("BUILD earlier");
#else
            Console.WriteLine("BUILD current");
#endif
            var action = args[0];
            if (action == "setup")
            {
                await database.Database.EnsureCreatedAsync(token);
                return 0;
            }

            if (action == "drain")
            {
                // Local workflow observation only: callers separately quiesce starts, signals and transport.
                var active = await database
                    .Set<WorkflowInstanceRecord>()
                    .CountAsync(
                        row =>
                            row.WorkflowType == "document-export"
                            && row.Status != WorkflowStatus.Completed,
                        token
                    );
                var pending = await database
                    .Set<InboxMessageRecord>()
                    .CountAsync(
                        row => row.SubscriptionKey == Subscription && row.ProcessedAt == null,
                        token
                    );
                Console.WriteLine($"DRAIN active={active} pending-replies={pending}");
                return active == 0 && pending == 0 ? 0 : 12;
            }

            if (action == "process")
            {
                var result = await scope
                    .ServiceProvider.GetRequiredService<IInboxProcessor<DeploymentDatabase>>()
                    .ProcessNextAsync(Subscription, token);
                Console.WriteLine($"PROCESS {result}");
                return 0;
            }

            var key = new WorkflowKey(Guid.Parse(args[1]));
            if (action == "read")
            {
                Print(await workflow.ReadAsync(key, token));
                return 0;
            }

            await using var transaction = await database.Database.BeginTransactionAsync(token);
            switch (action)
            {
                case "start":
                    var input =
                        JsonSerializer.Deserialize<ExportInput>(
                            await File.ReadAllTextAsync(args[2], token),
                            Json
                        ) ?? throw new JsonException("Input must not be null.");
                    Print(await workflow.StartAsync(key, input, token));
                    break;
                case "receive":
                    // Finite proof intake: a real transport must validate producer/admission and carry this metadata.
                    var row = await database
                        .Set<WorkflowInstanceRecord>()
                        .SingleAsync(
                            item => item.WorkflowType == "document-export" && item.Id == key.Id,
                            token
                        );
                    using (
                        var payload = JsonDocument.Parse(
                            await File.ReadAllTextAsync(args[2], token)
                        )
                    )
                    {
                        var reply = new IncomingMessage(
                            Guid.Parse(args[3]),
                            "renderer",
                            "export.prepared",
                            1,
                            payload.RootElement,
                            correlationId: key.Id.ToString("D"),
                            causationId: row.PendingCommandId!.Value.ToString("D")
                        );
                        await scope
                            .ServiceProvider.GetRequiredService<IInbox<DeploymentDatabase>>()
                            .ReceiveAsync(Subscription, reply, token);
                    }
                    break;
                case "signal":
                    Print(await workflow.SignalAsync(key, new ApproveExport(), token));
                    break;
                default:
                    throw new ArgumentException(
                        "Use setup, start, receive, process, read, signal or drain."
                    );
            }

            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            Console.WriteLine("COMMITTED");
            return 0;
        }
        catch (Exception failure)
        {
            // Do not print connection strings, input/payloads or provider diagnostics.
            Console.Error.WriteLine($"FAILED {failure.GetType().Name}");
            return 10;
        }
    }

    private static void Print(WorkflowSnapshot<ExportState>? snapshot) =>
        Console.WriteLine("SNAPSHOT " + JsonSerializer.Serialize(snapshot, Json));

    private static WorkflowDefinition<ExportInput, ExportState> Define(string mode)
    {
#if EARLIER_BUILD
        const int version = 1;
        const string continuation = "prepared";
        const int replySchema = 1;
#else
        var version = mode == "incompatible" ? 2 : 1;
        var continuation = mode == "missing-binding" ? "prepared-renamed" : "prepared";
        var replySchema = mode == "wire-break" ? 2 : 1;
#endif
        return new WorkflowBuilder<ExportInput, ExportState>("document-export", version, Json)
            .StartWith(
                (context, input) =>
                {
                    Console.WriteLine("METHOD start");
                    if (input.ExportId == Guid.Empty || input.Format != "pdf")
                        throw new InvalidDataException("Unsupported export input.");

                    return WorkflowTurn<ExportState>.Request(
                        new(input.ExportId, input.Format, "preparing"),
                        OutgoingMessage.FromPayload(
                            context.CommandId,
                            "export.prepare",
                            "export.prepare",
                            1,
                            new { input.ExportId, input.Format },
                            Json,
                            correlationId: context.Key.Id.ToString("D")
                        ),
                        continuation
                    );
                }
            )
            .OnReply<PreparedReply>(continuation, "export.prepared", replySchema, Prepared)
            .OnSignal<ApproveExport>(
                "approve",
                (_, state, _) =>
                {
                    Console.WriteLine("METHOD approve");
                    return WorkflowTurn<ExportState>.Complete(state with { Phase = "ready" });
                }
            )
            .Build();
    }

    private static WorkflowTurn<ExportState> Prepared(
        WorkflowContext context,
        ExportState state,
        PreparedReply reply
    )
    {
        Console.WriteLine("METHOD prepared");
        if (reply.ExportId != state.ExportId)
            throw new InvalidDataException("Outcome belongs to another export.");

        if (!reply.Succeeded)
            return WorkflowTurn<ExportState>.Complete(state with { Phase = "refused" });

#if !EARLIER_BUILD
        // A compatible additive field: omitted by the earlier writer, optional for its reader.
        state = state with { ReviewedByBuild = "current" };
#endif
        return WorkflowTurn<ExportState>.WaitForInput(
            state with
            {
                Phase = "awaiting-approval",
            },
            "approve"
        );
    }
}

internal sealed record ExportInput(
    [property: JsonRequired] Guid ExportId,
    [property: JsonRequired] string Format
);

internal sealed record PreparedReply(
    [property: JsonRequired] Guid ExportId,
    [property: JsonRequired] bool Succeeded
);

internal sealed record ApproveExport;

internal sealed record ExportState(
    [property: JsonRequired] Guid ExportId,
    [property: JsonRequired] string Format,
    [property: JsonRequired] string Phase
)
{
#if !EARLIER_BUILD
    public string? ReviewedByBuild { get; init; }
#endif
}

internal sealed class ExportReplies(IWorkflow<ExportInput, ExportState> workflow)
    : IInboxHandler<DeploymentDatabase>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken token)
    {
        if (message.ProducerKey != "renderer" || message.TenantKey is not null)
            throw new InvalidDataException("Unadmitted producer or tenant.");

        await workflow.HandleReplyAsync(new(Guid.Parse(message.CorrelationId!)), message, token);
    }
}

internal sealed class DeploymentDatabase(DbContextOptions<DeploymentDatabase> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureWorkflows("exports", "processes", "jsonb");
        modelBuilder.ConfigurePostgresOutbox("exports", "outgoing");
        modelBuilder.ConfigurePostgresInbox("exports", "incoming");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        ValidateChanges();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (Environment.GetEnvironmentVariable("WORKFLOW_DEPLOYMENT_PAUSE_AFTER_SAVE") == "true")
        {
            // Test cut after real database writes, before the inbox processor completes and commits.
            Console.WriteLine("TURN_SAVED_BEFORE_COMMIT");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return result;
    }

    private void ValidateChanges()
    {
        this.ValidateWorkflowChanges();
        this.ValidateOutboxChanges();
        this.ValidateInboxChanges();
    }
}
