# D2: domain events and required reactions

Date: 2026-10-14. The owner agreed this follow-up scope and asked to reconsider dispatch
within SaveChanges, given the existing owning DbContext validation/staging obligations.
The local increment is now prepared for review: [D2.1 scope](d2-1-local-domain-reactions.md),
[executable usage](../../samples/Wholesale/DomainEventsDemo/README.md) and
[fresh evidence](../reports/d2-1-local-domain-reactions.md). Its sample-owned SaveChanges handling
does not establish a generic dispatcher or universal write guard. D2.2 remains unimplemented.

On 2026-10-15 the owner authorized promoting the separately proven two-shape local mechanism into
[Gloaming.Domain and its optional EF adapter](../../src/Gloaming.EventReactions/README.md).
[D2.1b evidence](../reports/d2-1b-gloaming-domain-promotion.md) records the library implementation;
it preserves Sales' specialized protocol and does not complete D2.2.

The subsequent [registered integration cleanup](../reports/d2-1c-domain-event-integration-simplification.md)
replaces that specialized protocol with Gloaming registrations and ordinary Sales handlers, removing
the default tracked-row/fact mirror and aggregate save guard. Native tenant validation and aggregate
command concurrency remain; D2.2 is still unimplemented.

The owner subsequently selected [D2.1d module composition and sync/async interception](d2-1d-module-owned-event-composition.md).
It removes the explicit save service and ConsumerScope, uses ordinary module extensions/typed
handlers and retains native tracker acceptance. [Current evidence](../reports/d2-1d-module-owned-event-composition.md)
records callback ordering and transaction limits. This still does not implement D2.2.

## Domain meaning and separate increments

Owner clarification, 2026-10-15: retain bounded transactional intra-module event handling, with explicit
application orchestration recommended for complex dependency structures. Preserve Raise capture order
across participating sources; subscription routing is not business dependency scheduling. Required
local writes remain atomic. D2.2 is deferred until a concrete need for post-commit completion exists.
It is not the default merely because receivers are separate aggregates. Sagas and compensation policy
belong to the owning business module. D2.3 assesses reliable in-process calls through module Contracts.

Start from one concrete state-stored operation and a named reaction with business value.
Domain events describe private accepted decisions; they are not automatically event-sourcing
records or public integration contracts. A handler/application use case may load another
aggregate and invoke its behavior. The domain owns eligibility; coordination owns persistence
and the required transaction/recovery contract.

Deliver independently reviewable outcomes:

1. **D2.1 local required reactions:** collect newly accepted domain facts, handle one required
   local reaction and persist its participating state atomically. Compare explicit application
   coordination with an owning-context SaveChanges dispatch phase.
2. **D2.2 durable deferred reactions:** retain required work atomically with the producer change,
   then process the receiver reaction with restart, duplicate and failure proofs. Reuse existing
   inbox/outbox and, if actual multi-step progress needs it, continuation mechanics. An internal
   durable work contract and a public cross-module integration contract have different audiences;
   choose and version the required contract deliberately.
3. **D2.3 module consistency research:** compare durable choreography, durable orchestration and
   shared native transactions through module Contracts under the
   [separate research scope](d2-3-module-consistency-and-coordination-research.md).
   WF1/WF2 provide independently committed workflow evidence; the shared-transaction tradeoff
   has not yet received the same assessment. This is separate from D2.2 implementation.

These outcomes do not require a framework. Select the concrete operation,
handler responsibilities and supported usage before implementing or proposing a library surface.

## SaveChanges is a candidate boundary, not every write or commit

Existing contexts validate tracked tenant, audit, inbox/outbox, workflow and event-store changes
through native SaveChanges overrides. This makes that boundary worth assessing for domain-event
processing. It does not establish that the required reaction is merely another validation guard.
Handlers can add work, load/change other aggregates or raise further facts, and therefore have
ordering, reentrancy and failure obligations beyond the existing guards.

EF supports database writes that bypass its change tracker and SaveChanges. Identify every
supported write path for the selected operation:

| Path | Boundary to assess |
| --- | --- |
| SaveChanges() / SaveChanges(bool) | Cover sync dispatch/validation, including acceptAllChangesOnSuccess=false; define treatment of handlers that require asynchronous I/O. |
| SaveChangesAsync(CancellationToken) / SaveChangesAsync(bool, CancellationToken) | Cover async dispatch/validation and cancellation without sync-over-async deadlocks or a skipped handler path. |
| ExecuteUpdate / ExecuteDelete and their async forms | Execute directly without tracked aggregate/event collection or SaveChanges dispatch; query filters may still apply, but tracked-write guards and native tracked concurrency do not. |
| ExecuteSql* and direct DbConnection/provider commands | Do not execute a SaveChanges dispatch phase or reconstruct domain decisions automatically. They require their own explicit write/transaction protocol when used for business state. |
| Schema migrations, seed/setup and infrastructure lifecycle SQL | Distinguish these from domain commands; a lease claim or receipt-maintenance operation does not automatically represent a new aggregate fact. |

A SaveChanges override/interceptor is not a universal guard against native SQL. Choose whether
non-tracked business writes are outside the supported aggregate protocol or receive a separately
proved path that enforces the selected invariants, concurrency, required reactions and ownership.
Do not synthesize domain events by trying to infer business intent from arbitrary SQL or row diffs.
Restrict or detect bypasses where useful, while being explicit that a shared database credential
and direct provider access are not constrained by a DbContext hook alone.

Successful SaveChanges is not necessarily a committed operation: the caller may invoke it several
times inside one explicit transaction and later roll back. EF's implicit transaction starts in
the underlying save, so handler-issued writes before that save are not automatically enlisted by
placing the handler in an override. Decide whether handlers only stage tracked changes or require
an owning transaction already active before dispatch. Keep network publication and independently
committed external effects outside pre-commit required local handling; stage durable outgoing work
in the owning transaction instead.

## Event lifecycle and proofs

For D2.1 define and exercise:

- Registration of the logical aggregate's accepted facts, including when domain models and
  persistence rows are separate. Scanning EF entities cannot silently assume that every tracked
  row is an event-bearing root.
- No facts from rejected decisions, historical hydration, replay or ordinary reads.
- Handler ordering, further facts raised during handling, bounded completion/cycle behavior
  and nested SaveChanges. Compare an explicit single batch with a bounded draining phase;
  do not adopt an uncontrolled recursive aggregate-changing cascade.
- Placement of existing validation guards after handler-added work where needed; a handler
  must not add unvalidated tenant, audit, outbox or aggregate changes after the guard phase.
- Multiple saves in one transaction, save without events, and an unchanged repeat save:
  prevent duplicate local reactions while preserving later newly accepted facts.
- Save/handler/commit faults, cancellation and rollback. Define precisely when pending facts
  are consumed/cleared. If failures require disposing the context and re-deciding in a fresh
  operation, prove that contract rather than silently promising same-context retry.
- Both sync/async overload families, false acceptAllChangesOnSuccess, and supported bypass
  paths. Prove the selected boundaries with actual PostgreSQL writes and independent outcomes.

For D2.2 add producer commit/work-record atomicity, death after producer commit but before
delivery, receiver effect/receipt atomicity, duplicate delivery, competing processing, restart
and semantic idempotency. An event-sourcing fact being durable does not supply these reaction
guarantees; replay must not repeat historical business effects.

Assess a small pending-fact collection or callable dispatch/staging utility only against real
repeated correctness work. Domain event definitions, subscriptions, effect rules, disclosure,
integration mapping and business idempotency remain consumer-owned. No generic dispatcher,
automatic private-to-public mapping, compulsory aggregate base or new persistence runtime is
selected by this plan. Record tested/rejected/proposed mechanisms and sample/template findings;
state explicitly when no new reusable mechanism was proven.

## Primary reference evidence

- [EF Core saving approaches](https://learn.microsoft.com/en-us/ef/core/saving/): tracked
  saves and direct bulk writes are separate mechanisms.
- [ExecuteUpdate / ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete):
  immediate execution, explicit transaction grouping and caller-managed concurrency predicates.
- [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions):
  multiple saves may occur before an explicit commit; savepoints do not imply a final commit.
- [SaveChanges interception](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#savechanges-interception):
  save interception and SQL-command interception are distinct extension points.
- [EF Core 10.0.12 DbContext source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs):
  the convenience save overloads forward to the corresponding bool overloads; prove the
  selected consumer's override behavior rather than assuming every write reaches them.

References were checked on 2026-10-14. They support API and transaction constraints. The original
planning update ran no runtime tests; implementation evidence is separately recorded in the D2.1 report.
