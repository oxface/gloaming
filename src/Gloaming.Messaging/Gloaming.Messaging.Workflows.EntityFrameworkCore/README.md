# Gloaming.Messaging.Workflows.EntityFrameworkCore

Optional typed continuation runner with a provided module-owned workflow table, scalar optimistic
concurrency and mandatory native save validation. Dependencies are Workflows, the existing EF
messaging roles, EF Relational and DI abstractions. No Npgsql, broker, worker or generated harness.

Register `AddWorkflow<TDbContext,TStart,TState>`, configure `ConfigureWorkflows(schema, table,
jsonColumnType)` in the owning context, and install `ValidateWorkflowChanges()` in both native
save overrides alongside existing inbox/outbox guards. PostgreSQL JSONB is the verified provider.

The runner executes only the saved method, retains requested identities/outcomes and enqueues a
returned request through that context's typed outbox. Direct callers own native transaction,
save and commit; the existing inbox processor owns reply-attempt persistence. Use fresh scopes.

[Complete setup, guarantees, errors, limitations and code samples](../docs/workflows.md).
