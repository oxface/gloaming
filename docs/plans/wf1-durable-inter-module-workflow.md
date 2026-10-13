# WF1: durable inter-module workflow

Status: owner-approved checkpoint `e05ae58`, merged into `origin/main` as `161a33e`.
The original `feat/durable-inter-module-workflow` branch started from `4c0e0c5`, containing
owner-approved OBS1. No Rootbolt interface was changed.
The [slice report](../reports/wf1-durable-inter-module-workflow.md) records fresh verification.

## Actual consumers and bounded outcome

Inventory already accepts `IssueStockV1` through `StockIssueInboxHandler`. Its existing
native processing transaction commits stock events, the inline aggregate, audit, outgoing
`StockIssueRecordedV1` or `StockIssueDeclinedV1`, and inbox completion together. Replies
retain the command's delivery ID in envelope CausationId and its conversation in
CorrelationId. The reply payload's MessageId identifies the reply itself, not the command.

Sales currently owns customer profiles and has no durable workflow. Add one small Sales-owned
stock-issue request that tracks the outcome of an Inventory command. This is a process-manager
assessment, not a full sales-order fulfilment model. Existing Inventory refusal rules remain
unchanged. No reservation, release, compensation or distributed rollback is implied.

## Consumer journey and policy

1. Establish the admitted Organization and actor. Start a request with a caller-supplied
   request ID, stock-position ID, observed stock version, positive quantity and deadline.
2. In one explicit Sales EF transaction, persist the request and enqueue `IssueStockV1`
   with a stable command MessageId and request CorrelationId. Repeating the same request
   with identical inputs is idempotent; changed inputs under that identity are rejected.
3. Existing outbox publication and committed inbox intake deliver the command to Inventory.
   Inventory decides independently and queues its existing success/refusal reply.
4. Sales commits reply intake before acknowledging RabbitMQ. Its typed inbox handler
   establishes trusted receiver-owned context and validates producer, Organization, alias,
   schema, correlation, causation, stock identity and available decision fields. Sales
   persists the resulting state with inbox completion in one local transaction.
5. A separate consumer-owned deadline operation finds persisted overdue requests and marks
   them NeedsAttention. A later valid reply can still resolve them to Issued or Declined.
   A deadline is durable data; polling does not depend on a timer surviving process death.

Proposed states: AwaitingReply, NeedsAttention, Issued, Declined. Persist the request inputs,
command identity, deadline, version and accepted outcome. Version is a native EF concurrency
token. Replies and deadline scans can select different inbox/work rows but update the same
request; they must therefore handle request concurrency explicitly. A failed transaction is
retried in a fresh scope against current state, never by reusing its tracked entities.

Matching repeated outcomes, including a new delivery ID for the same semantic reply, do not
repeat a transition. Conflicting outcomes are rejected for investigation. Unknown requests,
wrong tenants and unrelated causation must not create or mutate progress. Exact admission and
refusal outcomes stay in the consumer. Correlation metadata does not authenticate a sender.

Timeout does not prove failure: stock might already be issued while its reply is delayed.
Do not cancel remotely, send the command with a new identity or compensate automatically.
The late-reply/deadline race must converge to the actual accepted outcome in either order.

## Dependencies and ownership

Reuse `Rootbolt.Messaging`, its EF/PostgreSQL packages, ActorIdentity, Tenancy and the existing
consumer codec where needed. Sales may reference Inventory.Contracts, never Inventory's
implementation. Each module retains its own typed context, schema, migrations and local
transaction. Cross-module messages use RabbitMQ in the proof composition; routing, permissions,
producer bindings and manual acknowledgement remain sample-owned.

Keep provided inbox/outbox hosted workers and callable processors. The small deadline poller
is Sales policy, not a Rootbolt scheduler. Startup does not migrate or seed: finite setup
remains explicit. No common transaction context, saga base class, registry or worker framework.

[Wolverine's saga documentation](https://wolverinefx.net/guide/durability/sagas.html), checked
2026-10-10, describes persisted state, identity and message-driven transitions. That supports
the shape of this assessment; its generated handlers and runtime persistence integration are
not dependencies or proof of Rootbolt behavior.

## Proposed file and behavior map

Paths below are repository-relative and describe the implemented change set.

| Files | Concrete change |
| --- | --- |
| `samples/Wholesale/modules/Sales/Sales.Contracts/IStockIssueRequests.cs` | Explicit start/read contracts, request/results and enums without persistence or execution-context types. |
| `samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueRequestRow.cs`, `StockIssueRequests.cs`, `StockIssueRequestMapping.cs` | Persisted business progress, idempotent start, native read, owned keys/version predicates and explicit local save/commit. The short read stays beside its existing consumer interface rather than gaining an extra forwarding class. |
| `samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueReplyAdmission.cs`, `StockIssueReplyHandler.cs` | Consumer codec/admission, outcome validation and semantic idempotency inside the existing inbox processor transaction. |
| `samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueDeadlines.cs`, `samples/Wholesale/WorkflowDemo/WorkflowPollingWorkers.cs` | Separate overdue-state lane with bounded native scans, concurrency handling and restartable polling. Hosting stays in the executable, so Sales gains no hosting dependency. |
| `samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs`, `SalesRegistration.cs`, `Sales.csproj`, additive migration and snapshot | Configure request state and owned inbox/outbox tables, guards, typed registrations, used dependencies; preserve existing profile behavior and migrations. |
| `samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessageAdmission.cs` | Admit the additional host-bound Sales producer alongside the existing demo producer; preserve existing commands and refusal rules. |
| `samples/Wholesale/WorkflowDemo/` | Executable setup/journey and native RabbitMQ publication/intake adapters for the two directions. Explicit worker host composition using existing library workers plus the deadline lane. |
| `samples/Wholesale/WorkflowDemo.Tests/` | Real PostgreSQL/RabbitMQ journey, rollback, semantic identity, competing transitions, deadline and process-restart proofs. |
| `tests/ArchitectureTests/`, `ModulithFoundry.slnx`, `.github/ci-paths.yml`, `.github/workflows/ci.yml` | Enforce Contracts-only module coupling, include projects and select the workflow proof with relevant Messaging/Wholesale changes. |
| Sample/module READMEs, `src/Rootbolt.Messaging/docs/capabilities.md`, root plan/status documents and WF1 report | Record executable usage, consumer/library split, fresh results and unsupported operations. |

No archived files, template preset, existing migration replacement or Rootbolt public API
change is in scope. Existing standalone inbox-only/outbox-only adoption remains independent.

## Proof obligations and extraction decision

Use real PostgreSQL for local atomicity and concurrency, real RabbitMQ for the two-direction
journey, and separate processes for death/restart claims. Exercise:

- Start rollback leaves neither progress nor a command; restart preserves a committed start.
- Inventory success and each refusal resolve the correct admitted Sales request.
- A rolled-back reply handler leaves retryable work and unchanged progress.
- Transport duplicates and semantic duplicates under different delivery IDs remain harmless;
  incompatible request reuse and conflicting replies are rejected.
- Different request rows progress independently; competing replies and deadline scans for
  one row cannot overwrite the resolved outcome or duplicate an outgoing command.
- Kill/restart after a committed request and after Inventory commits but before Sales applies
  its reply; retained messaging eventually resolves progress without another stock issue.
- Restart with an expired persisted deadline; apply late replies before/after its scan and
  prove the same final outcome. Never infer stock reversal from local expiry.
- Organization isolation and unrelated/malformed replies do not alter another request.

Run relevant existing Messaging, Inventory dispatch/event persistence, Sales persistence/HTTP,
architecture, formatting and analyzer checks. Keep old fixtures as compatibility evidence;
new workflow claims require the new proofs above.

No new reusable mechanism was proven by this implementation. Ordinary consumer state and
handlers compose with existing messaging. Compare any repeated technical
obligation with a materially different workflow before proposing a library extraction.
Compensation, cancellation, generic durable scheduling, poison redrive, retention, replica
partitioning and bootstrap/repopulation remain separate slices.
