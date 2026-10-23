# Gloaming.Messaging.Workflows.EntityFrameworkCore

One optional package for explicit durable-continuation definitions, typed return instructions,
detached snapshots and native EF execution. It supplies a module-owned workflow table, scalar
optimistic concurrency and mandatory native save validation. Dependencies are Messaging,
the existing EF messaging roles, Gloaming.EntityFrameworkCore, EF Relational and DI abstractions.
There is no Npgsql, transport or Hosting dependency.

Import `Gloaming.Messaging.Workflows` for definition/turn/snapshot types and
`Gloaming.Messaging.Workflows.EntityFrameworkCore` for native EF setup and execution. Both
namespaces are supplied by this assembly.

Use `WorkflowBuilder<TStart,TState>` to bind ordinary start/reply/signal methods and stable
wire names. Return `WorkflowTurn<TState>.Request`, `.Complete` or `.WaitForInput`. Business
branching, state, compensation, payload meaning and authorization remain consumer code.
Definitions and serializer options are frozen. The runner executes only the saved method;
there is no replay/graph engine, human-assignment model, scheduler or workflow worker.

Alternatively, use [shared save composition](../../Gloaming.EntityFrameworkCore/README.md):
ConfigureWorkflows declares the workflows requirement. Register AddWorkflowValidation<TDbContext>()
and configure native options with ConfigureGloamingCapabilities<TDbContext>(provider).
This replaces the workflow validator call in overrides; it does not register a runner or worker.

Register `AddWorkflow<TDbContext,TStart,TState>`, configure `ConfigureWorkflows(schema, table,
jsonColumnType)` in the owning context, and install `ValidateWorkflowChanges()` in both native
save overrides alongside existing inbox/outbox guards. PostgreSQL JSONB is the verified provider.

The runner executes only the saved method, retains requested identities/outcomes and enqueues a
returned request through that context's typed outbox. Direct callers own native transaction,
save and commit; the existing inbox processor owns reply-attempt persistence. Use fresh scopes.

[Complete setup, guarantees, errors, limitations and code samples](../docs/workflows.md).

## Packaging and consumer responsibilities

Definitions now carry this package's EF dependency. Domain decisions and module Contracts
can remain plain C#; compose bindings in the consumer implementation that selects native EF
execution. A future provider-free definition assembly needs a demonstrated adopter and a
reviewed executable seam; this package makes no alternate-runner claim.

Existing EF consumers keep their package reference and both namespaces. Definition-only
consumers replace `Gloaming.Messaging.Workflows` with this package. Definition types move
assembly while their CLR namespaces, persisted wire names, process rows and mappings remain
unchanged. No type-forwarding compatibility package is supplied.

The [package-only adopter](../../../tools/release/consumers/Workflows.cs) and
[compiled deployment tests](../tests/WorkflowDeploymentTests/DeploymentTests.cs) exercise
independent adoption and consumer reader compatibility.
