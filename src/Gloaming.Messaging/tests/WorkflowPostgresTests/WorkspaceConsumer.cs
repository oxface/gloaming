using System.Text.Json;
using System.Text.Json.Serialization;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.WorkflowPostgresTests;

// Tenantless ordinary application: an existing resource takes an inspection branch and is never released.
public sealed record SetupWorkspace(string Name, Guid? ExistingResource = null);

public sealed record WorkspaceState(Guid ResourceId, bool Created, string Phase)
{
    public string? Marker { get; set; }
}

public sealed record ResourceReply(
    [property: JsonRequired] Guid ResourceId,
    [property: JsonRequired] bool Succeeded
);

public sealed record RetryRelease;

public sealed class WorkspaceMethods
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal List<string> Invocations { get; } = [];
    internal bool InvalidRequest { get; set; }
    internal bool ThrowAfterCandidateMutation { get; set; }

    internal WorkflowDefinition<SetupWorkspace, WorkspaceState> Define(int version = 1) =>
        new WorkflowBuilder<SetupWorkspace, WorkspaceState>("workspace", version, Json)
            .StartWith(Start)
            .OnReply<ResourceReply>("created", "resource.created", 1, Created)
            .OnReply<ResourceReply>("inspected", "resource.inspected", 1, Inspected)
            .OnReply<ResourceReply>("activated", "resource.activated", 1, Activated)
            .OnReply<ResourceReply>("released", "resource.released", 1, Released)
            .OnSignal<RetryRelease>("retry-release", Retry)
            .Build();

    private WorkflowTurn<WorkspaceState> Start(WorkflowContext context, SetupWorkspace input)
    {
        Invocations.Add("start");
        var state = new WorkspaceState(
            input.ExistingResource ?? Guid.NewGuid(),
            input.ExistingResource is null,
            "pending"
        );
        return Request(
            context,
            state,
            state.Created ? "create" : "inspect",
            state.Created ? "created" : "inspected"
        );
    }

    private WorkflowTurn<WorkspaceState> Created(
        WorkflowContext context,
        WorkspaceState state,
        ResourceReply reply
    )
    {
        Invocations.Add("created");
        Validate(state, reply);
        if (ThrowAfterCandidateMutation)
        {
            state.Marker = "changed";
            throw new InvalidOperationException(
                "Consumer method failed after changing its detached candidate."
            );
        }

        return reply.Succeeded
            ? Request(context, state, "activate", "activated")
            : WorkflowTurn<WorkspaceState>.Complete(state with { Phase = "create-refused" });
    }

    private WorkflowTurn<WorkspaceState> Inspected(
        WorkflowContext context,
        WorkspaceState state,
        ResourceReply reply
    )
    {
        Invocations.Add("inspected");
        Validate(state, reply);
        return reply.Succeeded
            ? Request(context, state, "activate", "activated")
            : WorkflowTurn<WorkspaceState>.Complete(state with { Phase = "inspect-refused" });
    }

    private WorkflowTurn<WorkspaceState> Activated(
        WorkflowContext context,
        WorkspaceState state,
        ResourceReply reply
    )
    {
        Invocations.Add("activated");
        Validate(state, reply);
        if (reply.Succeeded)
            return WorkflowTurn<WorkspaceState>.Complete(state with { Phase = "ready" });

        return state.Created
            ? Request(context, state, "release", "released")
            : WorkflowTurn<WorkspaceState>.Complete(state with { Phase = "activation-refused" });
    }

    private WorkflowTurn<WorkspaceState> Released(
        WorkflowContext context,
        WorkspaceState state,
        ResourceReply reply
    )
    {
        Invocations.Add("released");
        Validate(state, reply);
        return reply.Succeeded
            ? WorkflowTurn<WorkspaceState>.Complete(state with { Phase = "compensated" })
            : WorkflowTurn<WorkspaceState>.WaitForInput(
                state with
                {
                    Phase = "release-refused",
                },
                "retry-release"
            );
    }

    private WorkflowTurn<WorkspaceState> Retry(
        WorkflowContext context,
        WorkspaceState state,
        RetryRelease signal
    )
    {
        Invocations.Add("retry");
        return Request(context, state, "release", "released");
    }

    private WorkflowTurn<WorkspaceState> Request(
        WorkflowContext context,
        WorkspaceState state,
        string action,
        string continueAt
    ) =>
        WorkflowTurn<WorkspaceState>.Request(
            state with
            {
                Phase = action,
            },
            OutgoingMessage.FromPayload(
                InvalidRequest ? Guid.NewGuid() : context.CommandId,
                "resource." + action,
                "resource." + action,
                1,
                new { state.ResourceId },
                Json,
                context.Key.TenantKey,
                context.Key.Id.ToString("D"),
                context.CauseMessageId?.ToString("D")
            ),
            continueAt
        );

    private static void Validate(WorkspaceState state, ResourceReply reply)
    {
        if (state.ResourceId != reply.ResourceId)
            throw new InvalidDataException("Outcome belongs to another business resource.");
    }
}

public sealed class WorkflowDatabase(DbContextOptions<WorkflowDatabase> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureWorkflows("operations", "processes", "jsonb");
        modelBuilder.ConfigurePostgresOutbox("operations", "outgoing");
        modelBuilder.ConfigurePostgresInbox("operations", "incoming");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateWorkflowChanges();
        this.ValidateOutboxChanges();
        this.ValidateInboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateWorkflowChanges();
        this.ValidateOutboxChanges();
        this.ValidateInboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

internal sealed class WorkspaceReplies(IWorkflow<SetupWorkspace, WorkspaceState> workflow)
    : IInboxHandler<WorkflowDatabase>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken) =>
        _ = await workflow.HandleReplyAsync(
            new(Guid.Parse(message.CorrelationId!), message.TenantKey),
            message,
            cancellationToken
        );
}

internal static class WorkflowProof
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static WorkflowDatabase Context(string connection) =>
        new(new DbContextOptionsBuilder<WorkflowDatabase>().UseNpgsql(connection).Options);

    internal static IWorkflow<SetupWorkspace, WorkspaceState> Runner(
        WorkflowDatabase db,
        WorkspaceMethods methods,
        int version = 1
    ) =>
        new EfWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(
            db,
            new EfOutbox<WorkflowDatabase>(db),
            methods.Define(version)
        );

    internal static async Task<string> Database(PostgreSqlFixture fixture)
    {
        var connection = await fixture.CreateDatabaseAsync(Token);
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    internal static ServiceProvider Provider(string connection, WorkspaceMethods methods)
    {
        var services = new ServiceCollection();
        services.AddDbContext<WorkflowDatabase>(options => options.UseNpgsql(connection));
        services.AddPostgresOutbox<WorkflowDatabase>();
        services.AddPostgresInbox<WorkflowDatabase>();
        services.AddPostgresInboxProcessor<WorkflowDatabase>(new(TimeSpan.FromMilliseconds(1)));
        services.AddWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(_ =>
            methods.Define()
        );
        services.AddInboxHandler<WorkflowDatabase, WorkspaceReplies>("workspace-replies");
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    internal static async Task<WorkflowSnapshot<WorkspaceState>> Start(
        string connection,
        WorkspaceMethods methods,
        WorkflowKey key,
        SetupWorkspace? input = null
    )
    {
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        var snapshot = await Runner(db, methods).StartAsync(key, input ?? new("workspace"), Token);
        await db.SaveChangesAsync(Token);
        await tx.CommitAsync(Token);
        return snapshot;
    }

    internal static IncomingMessage Reply(
        WorkflowSnapshot<WorkspaceState> snapshot,
        bool success,
        Guid? id = null
    ) =>
        new(
            id ?? Guid.NewGuid(),
            "resources",
            "resource." + snapshot.ContinuationName,
            1,
            JsonSerializer.SerializeToElement(
                new ResourceReply(snapshot.State.ResourceId, success),
                WorkspaceMethods.Json
            ),
            snapshot.Key.TenantKey,
            snapshot.Key.Id.ToString("D"),
            snapshot.PendingCommandId!.Value.ToString("D")
        );

    internal static async Task<WorkflowSnapshot<WorkspaceState>> Continue(
        string connection,
        WorkspaceMethods methods,
        WorkflowSnapshot<WorkspaceState> snapshot,
        bool success
    )
    {
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        var next = await Runner(db, methods)
            .HandleReplyAsync(snapshot.Key, Reply(snapshot, success), Token);
        await db.SaveChangesAsync(Token);
        await tx.CommitAsync(Token);
        return next;
    }

    internal static async Task Receive(ServiceProvider provider, IncomingMessage reply)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDatabase>();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<WorkflowDatabase>>()
            .ReceiveAsync("workspace-replies", reply, Token);
        await tx.CommitAsync(Token);
    }

    internal static async Task<InboxProcessingResult> Process(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<WorkflowDatabase>>()
            .ProcessNextAsync("workspace-replies", Token);
    }
}
