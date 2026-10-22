using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("GLOAMING_PACKAGE_TEST_CONNECTION_STRING is required.");
var databaseName = "gloaming_workflow_" + Guid.NewGuid().ToString("N");
var connection = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(connection.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
    await create.ExecuteNonQueryAsync();

try
{
    connection.Database = databaseName;
    var options = new DbContextOptionsBuilder<ProcessDatabase>()
        .UseNpgsql(connection.ConnectionString)
        .Options;
    ProcessDatabase Open() => new(options);
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    int starts = 0;
    int continuations = 0;
    var definition = new WorkflowBuilder<Start, Progress>("package.check", 1, json)
        .StartWith(
            (context, input) =>
            {
                starts++;
                return WorkflowTurn<Progress>.Request(
                    new Progress(input.Label, false),
                    OutgoingMessage.FromPayload(
                        context.CommandId,
                        "checks.perform",
                        "checks.perform",
                        1,
                        input,
                        json,
                        correlationId: context.Key.Id.ToString("D")
                    ),
                    "checked"
                );
            }
        )
        .OnReply<Decision>(
            "checked",
            "checks.completed",
            1,
            (_, progress, decision) =>
            {
                continuations++;
                return WorkflowTurn<Progress>.Complete(
                    progress with
                    {
                        Accepted = decision.Accepted,
                    }
                );
            }
        )
        .Build();
    IWorkflow<Start, Progress> Runner(ProcessDatabase database) =>
        new EfWorkflow<ProcessDatabase, Start, Progress>(
            database,
            new EfOutbox<ProcessDatabase>(database),
            definition
        );

    Check(
        typeof(WorkflowDefinition<Start, Progress>).Assembly
            == typeof(EfWorkflow<ProcessDatabase, Start, Progress>).Assembly,
        "Definitions and execution must be supplied by one assembly."
    );
    await using (var setup = Open())
        await setup.Database.EnsureCreatedAsync();

    var key = new WorkflowKey(Guid.NewGuid());
    var input = new Start("external consumer");
    await using (var rolledBack = Open())
    await using (var transaction = await rolledBack.Database.BeginTransactionAsync())
    {
        await Runner(rolledBack).StartAsync(key, input);
        await rolledBack.SaveChangesAsync();
        await transaction.RollbackAsync();
    }

    await using (var fresh = Open())
    {
        Check(await Runner(fresh).ReadAsync(key) is null, "Rollback must leave no process.");
        Check(
            await fresh.Set<OutboxMessageRecord>().CountAsync() == 0,
            "Rollback must leave no request."
        );
    }

    WorkflowSnapshot<Progress> pending;
    await using (var started = Open())
    await using (var transaction = await started.Database.BeginTransactionAsync())
    {
        pending = await Runner(started).StartAsync(key, input);
        await started.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    Check(
        pending.Status == WorkflowStatus.Waiting
            && pending.Version == 1
            && pending.PendingCommandId is not null,
        "A committed start must retain its pending continuation."
    );
    var reply = new IncomingMessage(
        Guid.NewGuid(),
        "checks",
        "checks.completed",
        1,
        JsonSerializer.SerializeToElement(new Decision(true), json),
        correlationId: key.Id.ToString("D"),
        causationId: pending.PendingCommandId!.Value.ToString("D")
    );
    await using (var continued = Open())
    await using (var transaction = await continued.Database.BeginTransactionAsync())
    {
        var completed = await Runner(continued).HandleReplyAsync(key, reply);
        Check(
            completed.Status == WorkflowStatus.Completed && completed.Version == 2,
            "The saved continuation must complete the second turn."
        );
        await continued.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    await using (var duplicate = Open())
    await using (var transaction = await duplicate.Database.BeginTransactionAsync())
    {
        var unchanged = await Runner(duplicate).HandleReplyAsync(key, reply);
        Check(unchanged.Version == 2, "A duplicate outcome must not advance the process.");
        await duplicate.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    await using (var fresh = Open())
    {
        var restored = await Runner(fresh).ReadAsync(key);
        Check(
            restored is { Status: WorkflowStatus.Completed, Version: 2, State.Accepted: true },
            "A fresh context must restore committed completion."
        );
        var request = await fresh.Set<OutboxMessageRecord>().SingleAsync();
        Check(
            request.MessageId == pending.PendingCommandId,
            "The original committed request must be retained."
        );
    }

    Check(
        starts == 2 && continuations == 1,
        "Only the rolled-back start is repeated; a retained outcome does not rerun its continuation."
    );
    Console.WriteLine(
        "PASS combined continuations: native rollback/commit, fresh recovery and duplicate outcomes"
    );
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName}", admin);
    await drop.ExecuteNonQueryAsync();
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed record Start(string Label);

sealed record Progress(string Label, bool Accepted);

sealed record Decision(bool Accepted);

sealed class ProcessDatabase(DbContextOptions<ProcessDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigurePostgresOutbox("package_workflow", "requests");
        model.ConfigureWorkflows("package_workflow", "processes", "jsonb");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateWorkflowChanges();
        this.ValidateOutboxChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateWorkflowChanges();
        this.ValidateOutboxChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
