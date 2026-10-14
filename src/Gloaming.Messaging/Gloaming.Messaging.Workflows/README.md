# Gloaming.Messaging.Workflows

Optional explicit durable-continuation definitions, typed return instructions and snapshots.
Depends only on Gloaming.Messaging and the BCL; no EF, transport, scheduler or replay engine.

Use `WorkflowBuilder<TStart,TState>` to bind ordinary start/reply/signal methods and stable
wire names. Return `WorkflowTurn<TState>.Request`, `.Complete` or `.WaitForInput`.
Business branching, state, compensation and payloads remain consumer code. Definition bindings
and serializer options are frozen; no graph is interpreted.

[Complete setup, protocol, limitations and executable adopters](../docs/workflows.md).
Persistence/execution is an independent opt-in [native EF package](../Gloaming.Messaging.Workflows.EntityFrameworkCore/README.md).
