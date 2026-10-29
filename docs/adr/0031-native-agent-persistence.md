# 0031: Native MAF state and visible conversation persistence

Date: 2026-10-29. Status: implemented for owner interface/implementation review.

Provide optional native MAF persistence rather than adapting its graph/session protocol to
Gloaming's existing durable continuations. Checkpoints, working history and visible entries
have different retention/read semantics. Immutable full working-history snapshots let a
checkpoint reference its exact context; restoring an earlier branch must not load latest
conversation messages. Text storage deliberately preserves native JSON metadata order.

Keep lifecycle helpers EF-free, history independent of workflow execution, and visible
transcript storage MAF-free. Consumers select each mapping in their own context/schema,
serialize conversation writes and own the native transaction. Stores flush tracked changes
without committing. Advanced in-memory state is discarded after rollback; external calls
cannot be rolled back and have no exactly-once guarantee.

This adds reusable native checkpoint/history and ordered transcript persistence. It adds no
agent runtime, graph DSL, scheduler, provider selection, business head/authorization model or
automatic retry. Generic visible envelopes retain explicit consumer type/version/payloads;
readable history and action/version admission remain module-owned. Snapshot copying trades
storage for direct restoration; pruning/incremental history and durable hosting are deferred.
See the [contracts and proof owners](../../src/Gloaming.Agents/README.md).
