using System.Text.Json;
using Gloaming.Agents.Workflows;
using Gloaming.Agents.Workflows.EntityFrameworkCore;
using Gloaming.Conversations.EntityFrameworkCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AgentPersistenceDemo;

public static class AdoptionJourney
{
    public static async Task RunJourneyAsync(
        string connection,
        CancellationToken cancellationToken = default
    )
    {
        var client = new RecordingClient();
        await using (var database = DemoContext.Open(connection))
        {
            // Disposable proof setup only. Real modules own versioned migrations and grants.
            await database.Database.EnsureCreatedAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            await LockAsync(database, cancellationToken);
            var head = new DocumentHead();
            database.Add(head);
            var events = await NativeWorkflowExecution.StartAsync(
                DemoWorkflow.Build(database, client),
                new DraftInput("Original context"),
                Manager(database, cancellationToken),
                cancellationToken: cancellationToken
            );
            DemoWorkflow.Capture(head, events);
            await Transcript(database)
                .AppendAsync(
                    Guid.NewGuid(),
                    "proposal",
                    2,
                    JsonSerializer.SerializeToElement(new { text = head.Proposal }),
                    DateTimeOffset.UtcNow,
                    cancellationToken
                );
            await transaction.CommitAsync(cancellationToken);
        }

        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            await LockAsync(database, cancellationToken);
            var head = await database.Set<DocumentHead>().SingleAsync(cancellationToken);
            var events = await NativeWorkflowExecution.ResumeAsync<ReviewRequest, ReviewResponse>(
                DemoWorkflow.Build(database, client),
                new(head.SessionId, head.CheckpointId),
                Manager(database, cancellationToken),
                head.RequestId,
                new(false, "Refine the document"),
                cancellationToken: cancellationToken
            );
            DemoWorkflow.Capture(head, events);
            await Transcript(database)
                .AppendAsync(
                    Guid.NewGuid(),
                    "proposal",
                    2,
                    JsonSerializer.SerializeToElement(new { text = head.Proposal }),
                    DateTimeOffset.UtcNow,
                    cancellationToken
                );
            await transaction.CommitAsync(cancellationToken);
        }

        int calls = client.Inputs.Count;
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            await LockAsync(database, cancellationToken);
            var head = await database.Set<DocumentHead>().SingleAsync(cancellationToken);
            var events = await NativeWorkflowExecution.ResumeAsync<ReviewRequest, ReviewResponse>(
                DemoWorkflow.Build(database, client),
                new(head.SessionId, head.CheckpointId),
                Manager(database, cancellationToken),
                head.RequestId,
                new(true),
                cancellationToken: cancellationToken
            );
            head.Accepted = (string)events.OfType<WorkflowOutputEvent>().Single().Data!;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using var fresh = DemoContext.Open(connection);
        if (
            calls != 2
            || client.Inputs.Count != calls
            || !client.Inputs[1].Contains("Original context", StringComparison.Ordinal)
            || (await fresh.Set<DocumentHead>().SingleAsync(cancellationToken)).Accepted
                != "Draft: Refine the document"
            || (await Transcript(fresh).ReadAsync(cancellationToken: cancellationToken)).Count != 2
        )
            throw new InvalidOperationException(
                "The committed native recovery journey did not preserve its expected result."
            );
    }

    public static CheckpointManager Manager(
        DemoContext database,
        CancellationToken cancellationToken = default
    ) =>
        NativeWorkflowExecution.Checkpoints(
            new EfCheckpointStore<DemoContext>(
                database,
                database.Partition,
                "document",
                cancellationToken
            )
        );

    public static EfConversationTranscript<DemoContext> Transcript(DemoContext database) =>
        new(database, database.Partition, "document");

    // Consumer-selected lock namespace for this one-document proof, not a library lock protocol.
    public static Task LockAsync(
        DemoContext database,
        CancellationToken cancellationToken = default
    ) =>
        database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(48201029)",
            cancellationToken
        );
}
