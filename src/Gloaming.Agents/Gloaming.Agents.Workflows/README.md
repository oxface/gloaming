# Native workflow turns

Thin helpers over MAF Workflows 1.26.0, with no EF dependency. Adopt them with an independent
native checkpoint store or the separately selected [EF store](../Gloaming.Agents.Workflows.EntityFrameworkCore/README.md).

```csharp
var checkpoints = NativeWorkflowExecution.Checkpoints(store);
var events = await NativeWorkflowExecution.StartAsync(
    graph, typedInput, checkpoints, observe: Observe, cancellationToken: token);
// Reconstruct the consumer's graph and store before a subsequent operation:
var continued = await NativeWorkflowExecution.ResumeAsync<ReviewRequest, ReviewResponse>(
    graph, selectedCheckpoint, checkpoints, selectedRequestId, response,
    observe: Observe, cancellationToken: token);
```

Start preserves the declared generic input type; avoid erasing it to `object`. Resume requires
the selected native request ID and exact declared request/response types. It checks native
port type identities because restored JSON can be structurally deserializable into unrelated
CLR request types. Other pending requests remain native requests with their original correlation.
No custom human-input protocol or business checkpoint/head selection is introduced.

The observer sees native restoration and continuation events. Start returns its turn's events;
resume returns continuation events after answering, excluding the initial restoration events.
Each turn drains through the native pending-request halt, including checkpoint completion;
the first request event alone is insufficient evidence of a retained checkpoint. Observers run
synchronously and must not block indefinitely. Their exceptions abort the turn.

Native workflow/executor exceptions are propagated after observation; a native workflow failure
without an exception produces a payload-free InvalidOperationException. Cancellation aborts
the turn and requests native run cancellation; stopping event enumeration alone does not stop
native work. Runs are asynchronously disposed. No failure becomes a successful business result.
Caller rollback/context disposal, cancellation cooperation of tools, uncertain effects and
recovery decisions remain explicit. No automatic retries or external-effect compensation occur.

`Checkpoints` constructs the native JSON manager with default reflection metadata and the native
out-of-order option, or caller-supplied serializer options. Consumers own custom type metadata,
graph identity/version compatibility and any AOT requirements. This is bounded in-process
execution, not a scheduler or durable host. See the [reconstructing consumer](../samples/AgentPersistenceDemo/README.md).
