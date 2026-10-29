using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace Gloaming.Agents.Workflows;

/// <summary>Bounded native in-process turns; graph, admission, head selection and transaction remain caller-owned.</summary>
public static class NativeWorkflowExecution
{
    /// <summary>Constructs MAF's JSON checkpoint manager without changing native serialization or routing.</summary>
    public static CheckpointManager Checkpoints(
        ICheckpointStore<JsonElement> store,
        JsonSerializerOptions? serializerOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(store);

        return CheckpointManager.CreateJson(
            store,
            serializerOptions
                ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                    AllowOutOfOrderMetadataProperties = true,
                }
        );
    }

    /// <summary>Starts with the declared input type and drains through the native pending-request halt.</summary>
    public static async Task<IReadOnlyList<WorkflowEvent>> StartAsync<TInput>(
        Workflow graph,
        TInput input,
        CheckpointManager checkpoints,
        Action<WorkflowEvent>? observe = null,
        CancellationToken cancellationToken = default
    )
        where TInput : notnull
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(checkpoints);
        cancellationToken.ThrowIfCancellationRequested();

        await using var run = await InProcessExecution.RunStreamingAsync(
            graph,
            input,
            checkpoints,
            cancellationToken: cancellationToken
        );
        return await Drain(run, observe, cancellationToken);
    }

    /// <summary>Restores the selected checkpoint, answers only the matching native typed request and returns continuation events.</summary>
    public static async Task<IReadOnlyList<WorkflowEvent>> ResumeAsync<TRequest, TResponse>(
        Workflow graph,
        CheckpointInfo head,
        CheckpointManager checkpoints,
        string requestId,
        TResponse response,
        Action<WorkflowEvent>? observe = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        await using var run = await InProcessExecution.ResumeStreamingAsync(
            graph,
            head,
            checkpoints,
            cancellationToken: cancellationToken
        );
        var waiting = await Drain(run, observe, cancellationToken);
        var request =
            waiting
                .OfType<RequestInfoEvent>()
                .Select(x => x.Request)
                .SingleOrDefault(x =>
                    x.RequestId == requestId
                    && x.PortInfo.RequestType.IsMatch<TRequest>()
                    && x.PortInfo.ResponseType.IsMatch<TResponse>()
                    && x.IsDataOfType<TRequest>()
                )
            ?? throw new InvalidOperationException(
                "The selected typed request is not pending at this checkpoint."
            );
        await run.SendResponseAsync(request.CreateResponse(response));
        return await Drain(run, observe, cancellationToken);
    }

    private static async Task<IReadOnlyList<WorkflowEvent>> Drain(
        StreamingRun run,
        Action<WorkflowEvent>? observe,
        CancellationToken cancellationToken
    )
    {
        var events = new List<WorkflowEvent>();
        try
        {
            await foreach (
                var item in run.WatchStreamAsync(blockOnPendingRequest: false, cancellationToken)
            )
            {
                observe?.Invoke(item);
                if (item is WorkflowErrorEvent error)
                    ExceptionDispatchInfo
                        .Capture(
                            error.Exception
                                ?? new InvalidOperationException(
                                    "The native workflow reported a failure without an exception."
                                )
                        )
                        .Throw();

                if (item is ExecutorFailedEvent failed)
                    ExceptionDispatchInfo
                        .Capture(
                            failed.Data as Exception
                                ?? new InvalidOperationException(
                                    "The native executor reported a failure without an exception."
                                )
                        )
                        .Throw();

                events.Add(item);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return events;
        }
        catch
        {
            await run.CancelRunAsync();
            throw;
        }
    }
}
