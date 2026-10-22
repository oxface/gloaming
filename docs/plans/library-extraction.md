# Library and sample extraction plan

Direction update, 2026-10-22: use the [library architecture audit](../reports/gloaming-library-audit.md)
for the current assessment and proposed next increments. The template/extraction sequence
and dated authorizations below retain historical context; they do not select further template
work or authorize the audit's proposed interface/removal changes. Current package contracts
remain with their library families until concrete changes are reviewed.

The owner has authorized the remaining v1 work through local commits and integration, stopping
before push. The [execution ledger](v1-integration-completion.md) records current status,
required evidence and the [sample/experiment consolidation](s1-sample-experiment-consolidation.md).
The [closure audit](../reports/v1-slice-closure-audit.md) is historical source assessment; inbox
retention and broker/shutdown closure are now merged.

## Current delivery status and next slice — 2026-10-17

The three outputs remain reusable libraries, configurable repository population, and samples.
[The strategy review](../reports/strategy-review.md) brought a bounded template rehearsal
forward from E10; that rehearsal is now complete and owner-approved.

| Capability | Current disposition |
| --- | --- |
| E1–E3 foundations and ingress | Retained checkpointed libraries and consumer proofs. |
| E4–E5 event utilities and native consumers | Retained checkpointed mechanisms and experiments; no replacement dependency selected. |
| E6.1 inline decision state | Checkpointed separately as `1ae13d4`; an experiment, not justification to proceed automatically to repair. |
| T1 state-stored template rehearsal | Owner-approved checkpoint `8ccf4c8`; two generated consumer proofs, event/messaging omission and bounded initial creation. |
| ES1 bounded event append | Owner-reviewed and checkpointed as `ab85ec9`; retained native store/append and consumer proofs. |
| Library family grouping | Owner-approved checkpoint `39c1ab3`; local docs/tests and preserved external template adoption. |
| ES2 single-stream rebuilding | Owner-approved [native EF replacement](es2-native-ef-simplification.md) and reader checkpoint `91542a6`: write-only store, independent full replay and native stream stamps. [ES2 report](../reports/es2-single-stream-rebuilding.md) retains separately dated proof results. |
| Bounded event-history reader | Owner-approved and checkpointed as `91542a6`; shared native prefix loading and consumer adoption. [Reader report](../reports/event-history-reader-extraction.md) records fresh checks independently of earlier ES2 evidence. |
| Explicit JSON payload upcasting | Owner-approved and checkpointed as `b597717`, including PassThrough, Inventory and standalone chained/nested adoption; [new evidence](../reports/event-payload-upcasting.md). |
| Initial library naming | Owner-authorized historical checkpoint covering the first nine reusable projects; [dated scope and verification](../reports/library-naming-checkpoint.md). No runtime capability added. |
| Gloaming library naming | Owner selected Gloaming on 2026-10-14 for all current reusable projects; [scope and current verification](../reports/gloaming-library-renaming.md). Umbrine is the future ERP product built on the template and Gloaming. No runtime capability added. |
| Transactional outbox O1 | Owner-reviewed and checkpointed as `2d7a865`; merged through PR #1 as `beafa4e`. [Implementation and executable adoption](../../src/Gloaming.Messaging/README.md); [fresh proofs and remaining gaps](../reports/outbox1-transactional-dispatch.md). |
| Durable inbox I1 | Owner-reviewed implementation checkpoint `ecab884`, followed by CI refinement `863456b`; merged through PR #2 as `0f4d8bf`. [Reviewed scope](inbox1-durable-intake-processing.md); [implementation evidence and remaining gaps](../reports/inbox1-durable-intake-processing.md). |
| Explicit transactional audit E9 | Owner considers the slice complete; checkpoint `18a76f8` is present in `origin/main`. [Reviewed scope](e9-explicit-transactional-audit.md); [library contract](../../src/Gloaming.Auditing/README.md); [verification and remaining gaps](../reports/e9-explicit-transactional-audit.md). |
| Separate worker hosts W1 | Owner-approved checkpoint `7b43201`, merged into `origin/main` as `94ca57a`. Existing inbox/outbox APIs compose separate native roles with explicit setup; [scope](w1-separate-worker-hosts.md), [usage](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/README.md) and [process proofs](../reports/w1-separate-worker-hosts.md). No new library interface or mechanism. |
| Durable message observability OBS1 | Owner-approved checkpoint `b44b5d5`, merged into `origin/main` at `4c0e0c5`, including the optional native OTel subscription adapter. [Local contract](../../src/Gloaming.Messaging/docs/observability.md), [scope/file map](durable-message-observability.md) and [fresh proofs](../reports/obs1-durable-message-observability.md). |
| Durable inter-module workflow WF1 | Owner-approved checkpoint `e05ae58`, merged into `origin/main` as `161a33e`: Sales-owned stock-issue progress, independent Inventory decisions and overdue-state recovery. [Scope/file map](wf1-durable-inter-module-workflow.md), [executable adoption](../../samples/Wholesale/Worker/Broker/README.md) and [fresh evidence](../reports/wf1-durable-inter-module-workflow.md). No new library interface or reusable mechanism. |
| Compensating workflow WF2 | Owner-approved [bounded continuation interface/replacement scope](wf2-durable-continuations.md), merged into fetched `origin/main` as `d674e39`. Optional core/EF packages, Sales continuation adoption and standalone branching PostgreSQL proofs; [new evidence](../reports/wf2-durable-continuations.md) supersedes the uncommitted fixed-sequence experiment. |
| Replayable source B1 | Closed by owner direction on 2026-10-14 as a [retention/contract assessment](../reports/b1-retention-and-contract-assessment.md). Experiment source, tests and patch artifacts removed; dated results and research retained. Revisit only for a later concrete receiver need; no new library mechanism. B2 implementation remains separate. |
| DDD sample patterns D1 | Committed as `041c64a`: [bounded scope](d1-ddd-sample-patterns.md), [executable comparison](../../samples/Wholesale/IntegrationTests/Scenarios/Ddd/README.md) and [proof/extraction report](../reports/d1-ddd-sample-patterns.md). Immutable state-stored Sales decisions compared with existing event-sourced Purchasing decisions; no new reusable mechanism or template preset. |
| DDD follow-ups D1.1 / D2 | Owner-agreed planning scopes: [rich relational aggregates](d1-1-rich-state-stored-aggregate.md) and [domain-event reactions](d2-domain-events-and-required-reactions.md). SaveChanges handling is proven for the sample local transaction, with native write bypasses and save-versus-commit limits. D1.1 is checkpointed and D2.1 local handling is prepared below; the owner subsequently authorized the standalone Domain extraction recorded below. |
| Rich state-stored aggregate D1.1 | Owner-approved [Sales draft/relational-child scope](d1-1-rich-state-stored-aggregate.md), [executable usage](../../samples/Wholesale/IntegrationTests/Scenarios/RichAggregate/README.md) and [fresh proof/extraction report](../reports/d1-1-rich-state-stored-aggregate.md), checkpointed as `f55d803`. Root-wide invariants/concurrency and explicit row staging stay sample-owned; no new reusable mechanism. |
| EventReactions consolidation | Owner-approved [cohesive reshaping](../reports/event-reactions-consolidation.md) of the local reaction experiments into Gloaming.EventReactions and its EF adapter. Plain records, composed buffers, explicit scoped selectors and ordered bounded reactions replace marker/base requirements. Independent model, application and PostgreSQL proofs accompany the mechanism. D2.1a–e reports retain superseded intermediate evidence; one final contract is reviewed. |
| Shared EF capability integration | Gloaming.EntityFrameworkCore supplies model-selected preparation/change detection/validation. Existing feature bindings and module composition remain separate from EventReactions. [Research](../reports/ef1-capability-interception-research.md), [MassTransit comparison](../reports/ef1-masstransit-applicable-patterns.md) and [original implementation evidence](../reports/ef1-1-capability-composition.md) remain dated records; the consolidation report is current. |
| Module consistency research D2.3 | Owner-selected [comparison and recommendations](../reports/d2-3-module-consistency-and-coordination.md) prepared for review against the [bounded scope](d2-3-module-consistency-and-coordination-research.md). Existing WF1/WF2 cover durable process state and messaging. The separately selected D2.3a now proves existing inbox/outbox composition through public intake Contracts; shared native transactions remain conditional on an atomic-acceptance requirement and a separate consumer proof. No new reusable mechanism or runtime behavior is added; D2.2 remains deferred. |
| Local durable module delivery D2.3a | Owner-selected [public-contract intake scope](d2-3a-local-durable-module-delivery.md), [broker-free executable](../../samples/Wholesale/Worker/Local/README.md) and [fresh adoption/proof report](../reports/d2-3a-local-durable-module-delivery.md). Module-owned fresh-scope intake composes existing inbox/outbox/workflow services, preserving independent commits and RabbitMQ usage. No new reusable mechanism, migration or template preset. |
| Native CQRS consumer patterns C1 | Owner-approved on 2026-10-15; present in fetched main `7ea6652`: [bounded scope](c1-cqrs-consumer-patterns.md), [executable consumer](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/README.md) and [dated proof/extraction report](../reports/c1-cqrs-consumer-patterns.md). Earlier uncommitted/staged statements describe that handoff. Sales joins/filters/counts/pages use native EF and typed Contracts, without resource-permission filtering; Inventory availability remains module-owned. No new reusable mechanism. Template generation remains separate. |
| Access/resource authorization A1 | [Assessment prepared for review](../reports/access-authorization-assessment.md), 2026-10-16, with [pinned primary sources](../reports/access-authorization-primary-sources.md). Recommend native resource checks/module-owned relational predicates initially; the separately authorized A1.1 consumer journey is recorded below. The assessment adds no engine adoption, runtime/library interface or new reusable mechanism. A2 receives authority/query input; RLS remains separate. |
| Membership administration A1.1 | Merged in fetched main `d35b3ee`: owner-authorized [consumer scope](a1-1-membership-suspension.md), [Access usage](../../samples/Wholesale/modules/Access/README.md) and [dated proof report](../reports/a1-1-membership-suspension.md). Narrow suspend/reactivate Contracts, current scoped administration grant, self/last-admin safeguards, retained grants, revision/Organization concurrency and atomic audit. Existing admission is reused; no library interface or new reusable mechanism. Earlier unstaged statements describe its handoff. |
| Tenant authorization / optional RLS A2 | [Assessment prepared for review](../reports/tenant-authorization-assessment.md), 2026-10-16, baseline `d35b3ee`, with [primary-source evidence](../reports/tenant-authorization-primary-sources.md). Corrects actor-before-native-handler ordering; tenancy remains later. Recommends late consumer permission checks/queries and optional restricted-role RLS proofs with explicit transaction and transport boundaries. No runtime integration, middleware reorder, library interface or new reusable mechanism. |
| Sales resource permissions A2.1 | Owner-selected [consumer implementation](a2-1-sales-resource-permissions.md) following A2: scoped positive draft Read/Edit grants, complete permission-filtered queries, actual module entry checks and late native HTTP adoption. [Sales usage](../../samples/Wholesale/modules/Sales/README.md) and [proof report](../reports/a2-1-sales-resource-permissions.md) record its bounded behavior. No RLS, library interface, mandatory Access/FGA dependency or template preset. |
| Optional Sales PostgreSQL RLS A2.2 | Owner-selected [consumer proof](a2-2-sales-postgres-rls.md) after A2.1 commit `b2f0d01`: explicit [CQRS mode](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md), five-table ENABLE/FORCE policies, restricted runtime credentials and native transaction-local setup. [Fresh report](../reports/a2-2-sales-postgres-rls.md) records role, raw/bulk and physical-pool/retry controls plus setting-forgery limits. Reads/existing-draft edits only; no default HTTP/worker adoption, library interface, new reusable mechanism or template preset. |
| Explicit PostgreSQL tenant context A2.3 | Owner-selected [extraction](a2-3-postgres-tenant-context.md) of A2.2's setup into [Gloaming.Tenancy.EntityFrameworkCore.Postgres](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md). One explicit DbContext extension, required consumer setting, immutable accessor and native transaction checks; independent PostgreSQL adoption and transferred Sales lifecycle proofs are recorded in the [report](../reports/a2-3-postgres-tenant-context.md). No automatic interceptor, policy generator, core/Persistence dependency or template preset. |
| Ownership-declared RLS A2.4 | Owner-requested research into `.HasTenantOwnership(...).WithRls()` and native interceptors, comparing Bytefish, Marten and EF integrations in the [primary-source note](../reports/a2-4-rls-capability-reference-research.md). The owner subsequently authorized implementation, retaining HasQueryFilter and requiring write protection. The optional [adapter](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md) and [implementation report](../reports/a2-4-ownership-rls-capability.md) record the initial session implementation; the owner-selected transaction refinement below replaces that wiring. Automatic migration scaffolding remains deferred. |
| Transaction-local RLS simplification A2.5 | Owner-selected refinement: keep the existing operation/request owner responsible for beginning transactions, and establish tenant context automatically. [Current adapter](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md), [implementation/proofs](../reports/a2-5-transaction-rls-simplification.md). One scoped native transaction/command interceptor, direct `.UseRls` installation and existing save capability; no native registry, session lifetime or connection-lease tracking. |

The [A2.6 Sales HTTP slice](a2-6-sales-http-rls.md) is owner-approved and checkpointed as
`521e9b3`, with [bounded adoption and verification](../reports/a2-6-sales-http-rls.md).
The selected [A2.7 AppHost continuation](a2-7-aspire-sales-rls.md) is owner-approved and
checkpointed as `192fe7d`: optional finite runtime-role provisioning and explicit API startup;
[fresh evidence](../reports/a2-7-aspire-sales-rls.md) is separate. The owner then selected
[A2.8 restricted Sales outbox dispatch](a2-8-sales-outbox-dispatch.md): an independent executable
role, a context limited to the existing queue and column-level delivery grants, using the
existing dispatcher/worker. [Consumer setup](../../samples/Wholesale/Worker/Broker/sales-outbox-dispatch.md)
and the [report](../reports/a2-8-sales-outbox-dispatch.md) separate cross-tenant queue authority
from business RLS. A2.8 is owner-approved and checkpointed as `087a008`.
The selected [A2.9 inbox continuation](a2-9-sales-inbox-rls.md) adds an optional WF2 processor per
admitted Organization, automatic operation contexts and existing native RLS/processor transactions.
[Consumer usage](../../samples/Wholesale/Worker/Broker/sales-inbox-rls.md) and the
[report](../reports/a2-9-sales-inbox-rls.md) cover atomic workflow/outbox/inbox effects and
command-specific enqueue protection that preserves global dispatch. Broader coverage, production
rollout, templates and library interfaces remain separately selectable.

The completed [E9 audit slice](e9-explicit-transactional-audit.md) deliberately excludes
correlation, causation and trace IDs under the owner's YAGNI decision; these remain messaging
and telemetry concerns. Its optional PostgreSQL mapping and item timeline belong to the
current [audit contract](../../src/Gloaming.Auditing/README.md).

On 2026-10-12 the owner selected bounded explicit continuations over replayed orchestration
and authorized preparing a concrete replacement of the uncommitted sequential experiment.
[The continuation proposal](wf2-durable-continuations.md) supplies public types, consumer code,
provided table/transaction protocol and exact replacement scope approved by the owner on
2026-10-12. The replacement is now merged at `d674e39`; earlier sequential reports remain
historical evidence. Existing inbox/outbox workers and native save/commit ownership are retained;
no graph interpreter or replay runtime is supplied.

Umbrine requested two assessment-only backlog items on 2026-10-14:
[A1 Access/resource authorization](a1-access-resource-authorization-research.md) and
[A2 tenant-aware composition/optional PostgreSQL RLS](a2-tenant-resource-authorization-rls-research.md).
The owner started A1 + A1.1 after C1 reached fetched main `7ea6652`. The
[comparative assessment](../reports/access-authorization-assessment.md) now supersedes the
[intake](../reports/access-authorization-research-intake.md) as current recommendation/evidence,
ahead of Access-specific template/readiness choices. It proposes native module-owned
authorization first and hands authority/query boundaries to A2. No engine adoption,
provider package, public interface or runtime implementation is selected. Access policy
remains consumer-owned and tenancy retains independent adoption.

On 2026-10-15 Umbrine added [A1.1 Organization membership suspension](a1-access-resource-authorization-research.md#a11-organization-membership-suspension-follow-up)
as a focused Access lifecycle follow-up. Existing active-membership admission is retained;
consumer-owned authorized suspend/reactivate operations, atomic business audit, concurrency
and combined read/write/reactivation proofs were assessed in A1. The owner then authorized
the focused [A1.1 implementation](a1-1-membership-suspension.md); its separately dated
[report](../reports/a1-1-membership-suspension.md) records fresh runtime proofs. It remains
consumer work with no separate pre-development gate or Tenancy-to-Access/FGA dependency.

A1.1 is now merged at `d35b3ee`. The [A2 assessment](../reports/tenant-authorization-assessment.md)
reconciles its code, actor policy establishment and raw worker SQL. Role/lifecycle research is
complete for review. A2.1 subsequently implements scoped Sales permission queries; A2.2
implements the optional bounded RLS proof recorded above. A general RLS package, default
HTTP/worker rollout and mandatory dependency remain unselected. A2.3 separately supplies
the selected explicit context utility; it does not supply a general RLS package or rollout.

The [remaining capability roadmap](remaining-capability-roadmap.md) is the current starting
point for selecting another slice. It explicitly includes DDD/CQRS assessments and template
patterns, separate worker hosts, observability, durable module workflows, snapshot/feed
repopulation, aggregate maintenance and a late cross-family isolation review. Its scope/order
is proposed; no new runtime interface is approved by that list.

## A2.4: ownership-declared PostgreSQL RLS

The owner authorized the fluent capability and then its transaction-local simplification,
retaining HasQueryFilter and requiring database write protection. The
[reference research](../reports/a2-4-rls-capability-reference-research.md) and
[earlier session implementation](../reports/a2-4-ownership-rls-capability.md) retain history.
The [current report](../reports/a2-5-transaction-rls-simplification.md) and
[package contract](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
describe the implemented interface under owner review:

```csharp
entity.HasTenantOwnership(
        row => row.OrganizationKey,
        () => RequiredOrganizationKey,
        "OrganizationScope")
    .WithRls(settingName: "app.tenant_key");

services.AddTenantOwnershipValidation<OrdersContext, string>(db => db.RequiredOrganizationKey);
services.AddPostgresTenantRowSecurity<OrdersContext>();
services.AddDbContext<OrdersContext>((provider, options) =>
    options.UseNpgsql(runtimeConnection).UseRls<OrdersContext>(provider));
```

The existing request/operation owner begins the native transaction once. EF's transaction-started
callback establishes parameterized local context before business SQL and before returning the
transaction to that owner. This removes explicit tenant-database setup at every transaction.
Reads on selected contexts require a transaction too; the owner explicitly selected retaining
that responsibility rather than adding a central request-level transaction owner. Existing HTTP
middleware already admits and initializes immutable tenancy once; workers establish each operation.

The feature uses one scoped native transaction/command interceptor plus the existing save capability.
No generic native-interceptor registry, connection-lease readiness flags or session settings remain.
Static model validation is cached per model, without tenant/services in its selection cache.
Direct native callback setup is necessary because EF has not assigned CurrentTransaction yet.
Setup failure disposes the unpublished transaction. Adopted/ambient transactions and unsupported
pooling modes are rejected; external poolers/provider failover need separate deployment proofs.

Consumer migrations explicitly install matching ENABLE/FORCE, USING and WITH CHECK policies.
Automatic policy scaffolding remains deferred. Scope immutability, admission, within-tenant
permissions, roles/privileges, complete table inventory and tenant-inclusive relationships remain
consumer-owned. The optional provider adapter introduces no mandatory Access/FGA dependency or
reverse dependency from core Tenancy/ownership. The explicit A2.3 utility remains independent;
Sales' existing opt-in local composition and default HTTP/worker/template paths are not changed.

The next [A2.6 Sales HTTP composition](a2-6-sales-http-rls.md) adopts the transaction-local
capability when a separate restricted Sales connection is selected. It covers six existing
ownership mappings, including profile audit, with finite policy/grant setup, actual two-role
startup checks and native request-owned read transactions. Existing varchar discriminators
are supported without a schema migration. [Current verification](../reports/a2-6-sales-http-rls.md)
is separate from earlier evidence; default AppHost/transport/template adoption remains deferred.

## B1: closed assessment and discarded implementation

Closed outcome, 2026-10-14: the owner accepted the
[retention and contract assessment](../reports/b1-retention-and-contract-assessment.md) and
closed B1 without further implementation. The record below describes the initial
experiment's findings, not an adopted default. Revisit the subject in a later slice with a real receiver
requirement before selecting storage, replay or state transfer. DDD and private event history
supply no automatic public integration contract. Closure does not approve commit or merge.
The owner subsequently rejected retaining the experiment code or reconstruction patch.
Source, tests, experimental migration, snapshot and preservation-specific configuration are
removed. Current Sales writes, Contracts, mapping and setup have no feed dependency.

The [closed scope](b1-replayable-integration-feed.md) and
[dated result report](../reports/b1-replayable-integration-feed.md) retain the protocol's
late-commit/gap/duplicate/restart findings and limits. Mechanism correctness did not establish
a business requirement for an Organization writer gate or a useful receiver projection.
No feed API, library interface or template preset remains. Any later implementation needs a
new reviewed consumer scope and fresh proofs; B2 snapshot/bootstrap is not automatically next.

## M2: inbox failure recovery (interface review)

On 2026-10-18 the owner selected failed inbox messages and authorized operator redrive as
one bounded inbox-only capability. The [concrete proposal](m2-inbox-failure-recovery.md)
traces processing/retry callers and presents consumer usage, public types, savepoint failure
semantics, schema/grant changes and PostgreSQL proofs. The owner approved implementation after
review. The [current family contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md) and
[slice report](../reports/m2-inbox-failure-recovery.md) record implementation/evidence prepared
for line-by-line review. Consumer classification, admission and operator authority remain policy;
outbox recovery, retention, payload editing and queued-schema evolution remain excluded.

The owner approved the [failure-accounting supervision follow-up](m2-inbox-failure-supervision.md)
on 2026-10-20. It implements a typed accounting fault and explicit worker/host supervision after
review found that the
previous broad worker catch could repeatedly select work whose failure could not be recorded.
No lease or schema change is added; consumer repair/restart policy remains explicit.

The owner checkpointed M2 as `f7d38d4`, then requested finishing inbox dead-letter operations
on the same branch before merge. The owner approved the [concrete follow-up](m2-inbox-dead-letters.md)
on 2026-10-21. Dead-letter terminology and one context-typed list/detail/retry interface use
the existing revision-checked transition. The [new report](../reports/m2-inbox-dead-letters.md)
distinguishes paging/metadata inspection proofs from the earlier recovery/supervision evidence.

On 2026-10-21 the owner selected the comparable outbox continuation. The
[concrete outbox proposal](m2-outbox-dead-letters.md) received owner interface/scope approval
before implementation. The [local contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md)
and [slice report](../reports/m2-outbox-dead-letters.md) describe lease-fenced failure accounting,
inspection/retry, schema/restricted-grant changes and new PostgreSQL/HTTP/process proofs. Attempts
retains its committed-claim meaning; publication/commit uncertainty does not establish
transport rejection. Existing inbox contracts remain intact, with only matching cursor
internals consolidated. Classification, budgets, acceptance definitions and operator authority
remain consumer-owned. No template preset or generic queue framework is added.

After approving that implementation, the owner authorized checkpoint `6bcdf9c` and selected
[completion-failure supervision](m2-outbox-completion-supervision.md) on 2026-10-21.
OutboxCompletionException carries MessageId and the original InnerException after publication
returns normally but completion is unconfirmed. The optional worker faults without another
poll; native host options and consumer repair/inspection/restart govern recovery. Existing
lease fencing, failure counters and conditional operator retry retain their semantics.
No schema, grant, dependency, recovery interface or worker-option change is needed. The
[separate report](../reports/m2-outbox-completion-supervision.md) records fresh evidence for
line-by-line review, including the completion-stage cancellation contract change.

The owner approved and checkpointed completion supervision as `020a292`, then selected
[outbox operational visibility](outbox-operational-visibility.md). Its review surface is a
detached OutboxBacklogSnapshot and ReadPostgresOutboxBacklogAsync context extension: exact-route
counts and oldest eligible age using one database statement time, without dispatch/recovery
registration or mutation. Native sample health checks and cached gauges retain consumer policy;
restricted Sales independently adopts the same read. The [current family contract](../../src/Gloaming.Messaging/docs/outbox-observability.md)
and [slice report](../reports/outbox-operational-visibility.md) record fresh proof boundaries.
No health/recovery framework, schema/grant changes or template preset is introduced.

After approving visibility checkpoint `2125860`, the owner selected
[bounded completed-outbox retention](outbox-completed-retention.md). The callable PostgreSQL
context extension admits an exact route, explicit completion cutoff and batch size 1–1000,
returning the observed committed deletion count. A separate finite Exports maintenance role
and consumer-owned partial index/migration exercise adoption. Native grants/RLS remain
authority; dispatch credentials are unchanged. Deletion removes sender identity/evidence
history without altering inbox lifetime or stopping obsolete publishers. The [family contract](../../src/Gloaming.Messaging/docs/outbox-retention.md)
and [new report](../reports/outbox-completed-retention.md) record current boundaries and proofs.

## M2 remaining scope: inbox retention and deduplication expiry

On 2026-10-22 the owner clarified that completed M2 sub-slices do not close the block:
inbox retention/deduplication expiry belongs to M2 before v1. The
[current-code assessment](../reports/m2-inbox-retention-assessment.md) and
[concrete proposal](m2-inbox-retention.md) recorded the preparation findings and review scope.
The owner subsequently approved implementation.
The [slice report](../reports/m2-inbox-retention.md) records processed-only cleanup, preserved
unprocessed/dead-lettered work, intake safety across deletion/RLS visibility and receipt-generation
conditions on the existing retry interface. An expired receipt permits old delivery to execute
again. Consumer duration/scheduling remain explicit. The owner approved checkpoint 6510f9e
and the five explicit post-v1 supplied-feature deferrals; retention merged as 2e1ffac.
The selected broker recovery/shutdown obligations below still require their own closure review.

## W1/M2 broker recovery and W1/OBS1 shutdown closure

After inbox-retention merge 2e1ffac, the owner selected the orchestration closure-audit's
live broker-recovery and stalled-publication shutdown obligations inside the existing worker/
messaging block. The [current-code assessment](../reports/w1-broker-recovery-shutdown-assessment.md)
and [concrete consumer scope](w1-broker-recovery-shutdown.md) received owner implementation
approval. The [closure report](../reports/w1-broker-recovery-shutdown.md) records the current
implementation and fresh evidence for change-set review. Terminal native session loss stops
dispatch/intake with explicit failure status; external supervision starts fresh sessions.
Ordinary publisher errors against an open session retain existing retry/accounting. Native
continuation and teardown settings close the selected resource-alarm stop wait. Real broker
application outage/recovery, retained progress and blocked-publication SIGTERM are exercised;
the existing unavailable-collector case gains a separate stop budget. No library-interface,
schema, transport-manager or arbitrary-publisher deadline change.

## RabbitMQ session lifecycle extraction (interface review)

After the consumer-only recovery/shutdown proof, the owner requested assessing how Gloaming
can supply reusable mechanics while consumers retain policy. The
[actual-adopter comparison](../reports/rabbitmq-session-lifecycle-assessment.md) identifies
shared native resource ownership/cleanup and optional terminal observation, with different
finite, fail-stop and native automatic-recovery lifetimes. The
[complete public interface](rabbitmq-session-lifecycle.md) received owner implementation
approval: optional Gloaming.Messaging.RabbitMQ, explicit native phase timeout options,
owned native channels and an intentional-stop token/callback observer, without Generic Host
or core/EF dependencies. Native publish/intake, topology, process authority and recovery choice
stay with the consumer. The [extraction report](../reports/rabbitmq-session-lifecycle.md) records
new public-session proofs and actual adoption in the worker, finite MessagingJourney and
three-channel WorkflowBroker. The latter retains native automatic recovery without terminal
observation. These new executions establish reusable lifecycle mechanics; the earlier
consumer-only test results remain separately dated evidence. No core/EF schema or protocol change.

## Queued-message schema compatibility

After squash-merging the outbox series and CI fixes into origin/main `86331de` (PR #41),
the owner selected and approved the [concrete consumer scope](queued-message-schema-compatibility.md)
after the [current-caller assessment](../reports/queued-message-schema-compatibility-assessment.md).
Exports keeps default v1 writes and explicitly selects v2, renaming Pages to PageCount.
Rendering independently adopts the existing optional JSON codec and preserves original
envelopes for deduplication. HTTP/RabbitMQ, conditional retry after reader replacement and
actual worker recovery are recorded in the [fresh report](../reports/queued-message-schema-compatibility.md).
The [family contract](../../src/Gloaming.Messaging/docs/schema-compatibility.md) owns deployment,
rollback and compatibility limits. No new library mechanism, schema migration or template file
was added; producer, admission, classification and operator authority remain consumer-owned.

## Retained delivery context

The existing [durable message observability proposal](durable-message-observability.md)
covers separate processes, retained W3C context, native RabbitMQ/OTel wiring and fault proofs.
Its owner-reviewed interface/schema extensions and native instrumentation are implemented;
the OBS1 report records fresh process, export, PostgreSQL and compatibility proofs.
[Audit context findings](../reports/e9-audit-context-research.md) and the new
[capability reference review](../reports/remaining-capability-reference-review.md) distinguish
source evidence from future extraction candidates. Both messaging capabilities and audit are
completed; their dated reports remain historical evidence, not proof of these follow-ups.

O1 narrowed E7 below to outbox mechanics, with a state-stored standalone adopter and an
event-sourced Inventory consumer. It includes a simple opt-in hosted outbox worker and
consumer-owned RabbitMQ publication proof. Durable inbox intake, transactional processing
and a full module-to-module acknowledgement sample are implemented in I1.
Reuse the existing codec/ownership utilities at consumer composition points;
no event-sourcing dependency or automatic domain-event publication is
proposed for the outbox. Owner follow-up groups inbox/outbox in `Gloaming.Messaging` and
names the provider package `Gloaming.Messaging.EntityFrameworkCore.Postgres`. The implemented
small provider-free Messaging package supplies the used envelope/publisher contract;
`Gloaming.Messaging.EntityFrameworkCore` contains used row/mapping/enqueue/save validation,
typed contracts and optional worker, while PostgreSQL supplies native model specialization
and claim/completion SQL. No generic SQL dialect or second-provider guarantee is proposed.
Independent inbox opt-in is implemented in I1. Each module owns its context, tables and
typed dependencies; receiving work commits separately from sending work. Transport
implementation/configuration remains consumer-owned. Existing Persistence ownership utilities
and Events codec remain independently composed, with no relocation or mandatory dependency.
Native EF coordinates a single owning transaction; no Gloaming root package or universal delivery/
transaction framework is proposed.

T1 implements one fixed event-free Catalog/console composition with configurable application
name/root namespace, local library source snapshots and TypeScript/npm creation tooling around
native `dotnet new`. See [the creator](../../tools/template/README.md) and
[checkpoint findings](../reports/t1-template-rehearsal.md) for supported platforms and limits.
It does not implement optional event presets, production ingress or repository updates.

ES1 concentrates accepted-batch technical append using the existing state-loading paths.
Inventory issues depend on loaded availability; the independent counter uses captured history
and direct JSON without tenancy or required views. Domain policy and native save/commit remain
consumer-owned. [Its report](../reports/es1-bounded-event-append.md) distinguishes new proofs
and source-concentration leverage from historical evidence. Existing demos, fixtures,
migrations, the frozen archive and T1 output are preserved; no cleanup is included.
Marten remains a behavioral reference for a smaller optional capability, not a selected
runtime dependency. The approved native EF direction remains in force.

The owner requested and approved replacement of per-call delegates/timestamp assembly with
an aggregate-oriented journey. The package-free aggregate core and configured EF appender
are now implemented; [ADR 0005](../adr/0005-aggregate-write-contract-and-native-append.md)
records the required write contract and optional inheritance. Pure reducers remain in modules
and are shared with existing historical reconstruction. The revised report records actual
verification separately from the initial surface. No generic reader/projector engine or subsequent event slice follows from ES1 approval;
ES2 now has its own explicit owner-reviewed scope.

The [owner follow-up and deferred capability catalog](event-sourcing-capabilities.md) records
transactional main-state consistency, native query/filter naming, event registry/JSONB findings
and future proof obligations. Mandatory main state applies to registered aggregate writes;
raw streams remain permitted. The owner subsequently endorsed IEventStore<TAggregate> and
authorized concrete changes. [The provided write-store surface](es1-library-write-store.md)
now concentrates native lookup, version observation/comparison, context/transaction association,
main-state loading and configured required-state staging. Explicit native save validation
checks tracked required participants; no generated persistence or hidden save/commit.
[New executions](../reports/es1-library-write-store.md) accompany implementation review.

The owner-approved default envelope and projection/store terminology refinements are now
implemented. [Library-local capability records](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md)
retain the supported contract, bounded rebuilding/remaining async/multi-stream context and provider
adapter investigation. Other packages retain their own local setup/limits/deferred directions.
The default-envelope refinement did not move projects; see [its proof and scope report](../reports/es1-envelope-and-library-docs.md).
The subsequent owner-authorized [family relocation](library-family-layout.md) groups all five
library families with local documentation and tests. ES2's initial optional Postgres adapter
was subsequently replaced by the reviewed native concurrency implementation.

ES1, family relocation, native ES2 and the bounded reader are owner-approved checkpoints.
[ES2 single-stream rebuilding](es2-single-stream-rebuilding.md) corresponds to part of the
deferred E6.2 direction. Payload upcasting is checkpointed as `b597717`; reusable-library
renaming and the CI family split are checkpointed. The E0–E10 sections below preserve the original
sequence and evidence; they do not authorize broader repair, async processing or E7.
The 2026-10-08 transport direction also supersedes their optional library-adapter candidate:
transport implementations stay consumer-owned; later integration examples may prove their
composition with the Messaging contracts without introducing a library bus runtime.

## Bounded event-history reader (owner-approved)

The owner asked to see the extracted implementation rather than defer it indefinitely.
[The concrete reader proposal](event-history-reader-extraction.md) includes its public types,
native EF implementation, complete-key/version guarantees, consumer adoption and exact file map.
It concentrates duplicated prefix queries, range/endpoints checks and decoded-event loading from
Inventory, Purchasing and the tenant-free raw counter. Temporal selection, aggregate evolution,
payload decoding, tenant admission and final save/commit remain consumer-owned.

The owner approved the interface and exact scope. The implementation adds one dependency on
existing Events.History integrity checks and reader injection for the independently registered
rebuilder. It adds no serialization/provider dependency, projector engine or automatic catch-up.
[The slice report](../reports/event-history-reader-extraction.md) records its verification and
consumer complexity removed, separately from historical ES2 evidence. The reader is checkpointed
as `91542a6`; subsequent upcasting is checkpointed as `b597717`.

## ES3: optional regeneration executor (interface review)

Status: owner approved the initial executor interface on 2026-10-17 and the ergonomics revision
on 2026-10-18. The current reviewed surface below is implemented; the stock-position executable
adopts it, and a tenant-free ledger exercises independent use.
See the [scope, usage, failure rules and proof matrix](es3-aggregate-regeneration-operations.md#executor-proposal-for-owner-review).

The addition lives in `Gloaming.EventSourcing.EntityFrameworkCore`. It provides one
complete regeneration operation over the existing provided rebuilder. Its own native transaction
is explicit in this operation's contract; existing `IAggregateRebuilder` remains a staging
operation with caller-owned save/commit. There is no ambient or cross-module transaction adoption.

Owner-reviewed public surface (declaration overview; package source defines the implementation):

```csharp
public sealed class AggregateRegenerator<TAggregate, TContext>
    where TAggregate : class
    where TContext : DbContext
{
    public AggregateRegenerator(IServiceScopeFactory scopes);

    public Task<AggregateRegenerationResult> RegenerateAsync(
        Guid id,
        AggregateRegenerationOptions options,
        IProgress<AggregateRegenerationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record AggregateRegenerationOptions
{
    public int MaxAttempts { get; init; } = 1;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.Zero;
}

public enum AggregateRegenerationStatus
{
    Completed = 1,
    NotFound = 2,
    Conflict = 3,
    Cancelled = 4,
    Failed = 5,
    Indeterminate = 6,
}

public sealed record AggregateRegenerationResult(
    AggregateRegenerationStatus Status,
    int Attempts,
    AggregateRebuildResult? CapturedHead,
    Exception? Error);

public enum AggregateRegenerationPhase
{
    Started = 1,
    Replayed = 2,
    Retrying = 3,
}

public sealed record AggregateRegenerationProgress(
    int Attempt,
    AggregateRegenerationPhase Phase,
    AggregateRebuildResult? CapturedHead = null,
    Exception? Error = null);
```

The constructor's native scope factory returns a fully initialized scope for each attempt.
Inventory supplies a consumer-owned decorator that establishes the already-authorized immutable
tenant before returning a scope or resolving its DbContext. The factory disposes its scope on
initialization failure; asynchronous admission completes before invoking regeneration. Independent
consumers without operation context use the ordinary native factory. EventSourcing gains no
tenancy/actor dependency or new scope abstraction.

The concrete class owns scope creation, resolves `TContext` and `IAggregateRebuilder<TAggregate>`
from that scope, verifies their existing private native maintenance association after replay,
then saves/commits. It can be constructed directly or registered with native DI; no new factory,
registration builder, public scope abstraction, executor interface or hosted service is introduced.

The consumer chooses positive `MaxAttempts` and a constant nonnegative `RetryDelay`; the sample
selects three attempts and 100 ms between attempts. Invalid options/empty IDs/null dependencies
throw native argument errors before an attempt. Delay must fit the native `Task.Delay(TimeSpan)`
range. Other operation failures return the typed result after cleanup. Cancellation before the
first attempt returns `Cancelled` with zero attempts. A target that is not found is terminal.

The executor calls `progress.Report` in phase order: start after an initialized scope is returned,
successfully staged full replay, and retry after disposal. Delivery belongs to the IProgress
implementation. The CLI uses an inline reporter; standard Progress<T> may deliver asynchronously.
Terminal outcomes are returned, not sent to a post-commit observer.
The host owns invocation/target identity, timestamps, JSON/text formatting, logging and exit codes.
A synchronous Report failure before commit aborts the attempt and returns `Failed`. An asynchronous
handler error is outside the executor's returned outcome. No Report call is made after an
acknowledged commit. `CapturedHead` describes replay metadata, never independent
proof of durability. An acknowledged commit returns `Completed`; a commit attempt without an
acknowledgment returns `Indeterminate`, including cancellation, with no automatic retry. Cleanup
errors retain the known commit outcome and are available in `Error`; they do not turn an
acknowledged commit into an unknown one. Error values are diagnostic exceptions for the host to
format deliberately, not messages automatically written to stdout.

Automatic retry recognizes only the provided rebuilder's verified changed-header race and native
`DbUpdateConcurrencyException` entries from that attempt's exact private maintenance association.
Scope-factory/Report exceptions, unrelated concurrency entries, arbitrary SQL constraints and
integrity/decoding failures are not retryable. No message matching, consumer-wide predicate or
provider dependency is added. Exhausted recognized contention returns `Conflict`; other pre-commit
failure returns `Failed`. The [detailed scope](es3-aggregate-regeneration-operations.md#executor-proposal-for-owner-review)
specifies the ahead-state check and the remaining provider-constraint limitation.

This implementation supersedes the initial assessment that all runner logic must stay consumer-owned.
The reusable execution obligation is now exercised through both consumers; retry values,
admission and host presentation remain consumer policy. The [slice report](../reports/es3-aggregate-regeneration-operations.md)
records new library adoption separately from the initial consumer-only executions.

### ES3 ergonomics follow-up (approved, 2026-10-18)

The owner requested alternatives to per-call progress and scope-initialization callbacks and
approved this replacement. The surface above and the declaration below are now current:

```csharp
public Task<AggregateRegenerationResult> RegenerateAsync(
    Guid id,
    AggregateRegenerationOptions options,
    IProgress<AggregateRegenerationProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

Use native `IProgress<T>` for typed progress. The executor calls `Report` in phase order, while
delivery belongs to the reporter. A synchronous console reporter can flush ordered JSON directly;
a UI may choose the standard `Progress<T>`. Standard `Progress<T>` dispatches via its captured
SynchronizationContext or the thread pool, so callbacks need not finish before the operation
returns and their asynchronous exceptions cannot be converted into its returned outcome. A
synchronous exception from `Report` before commit can still produce Failed. Terminal completion
remains the returned result, independent of notification delivery. No new reporter abstraction
or background queue is introduced. See [IProgress<T>](https://learn.microsoft.com/en-us/dotnet/api/system.iprogress-1)
and [Progress<T> dispatch](https://learn.microsoft.com/en-us/dotnet/api/system.progress-1#remarks).

The per-call initialization callback is removed; the constructor still receives native
`IServiceScopeFactory`. Inventory supplies a consumer-owned decorator that
creates a native scope, publishes the already-authorized immutable tenant (and any required
actor context), then returns the scope. It must dispose that scope if initialization fails.
Each retry invokes the factory again; no operation context is rebound. Independent ledger
adoption continues to supply the ordinary native scope factory.

The existing Inventory technical entry constructs that initialized-scope factory once for
the selected immutable operation context, then constructs the executor. This keeps the
aggregate internal and removes per-call service-provider initialization code from execution.
There is no new Gloaming scope-factory interface, provider dependency or scope catalog.
Started means scope creation/initialization finished; initialization failure returns
Failed without a Started notification. Counts still include that attempted scope creation.

Revised proofs exercise both reporters and factory failure cleanup, as well as fresh scopes,
disposal-before-retry, typed context pairing, native conflict classification and commit certainty.
Native DI owns scoped dependencies; the consumer factory owns failure cleanup before returning
a scope. Both PostgreSQL adopters and process interruption proofs are included in the
[dated verification report](../reports/es3-aggregate-regeneration-operations.md).

## Current ES2: reviewed native EF replacement

The owner approved [the exact interface/file/behavior scope](es2-native-ef-simplification.md)
on 2026-10-07. Writing and maintenance are separate implementations, using native header
Version/ConcurrencyStamp concurrency. Remove public Prepare/Stage handles, mandatory write-side
history bindings, cooperative gates, the provider package and dormant summary storage.
State/event encoding finishes before tracked changes; native save/commit remains caller-owned.
The main inline aggregate stays at the head. Native Queries/Filters and consumer live views are
the read side. Catch-up and other persisted views remain deferred. The later owner-selected
[ES3 command](es3-aggregate-regeneration-operations.md) composes this existing maintenance role
for one stock position, including finite retries/progress/cancellation/restart. Its later
owner-approved optional executor supplies the complete operation described above; durable
maintenance queues/scheduling remain deferred.

The sections below preserve earlier review history, not additional current configuration or
public surfaces. [ADR 0008](../adr/0008-native-optimistic-aggregate-rebuilding.md) records the
settled choice; [the ES2 report](../reports/es2-single-stream-rebuilding.md) records new versus
historical execution evidence. Leave new changes unstaged and preserve the existing index;
exact complete commit approval remains separate.

## ES2 owner-review follow-up (historical refinement)

Status: owner-authorized refinement, implemented and verified, 2026-10-07.
The owner reviewed the proposal below and instructed fixing the obvious review comments.
Shared aggregate registration and default rebuilding are now implemented; the full changes
remain available for line-by-line review. The retained SQL diagnostic
has separately been translated to TypeScript and verified, and the repository workflow records
the owner's scripting preference. The refinement simplifies setup for the existing mechanism;
it introduces no additional repair algorithm or background processing.

The concrete consumer setup is:

```csharp
// Module DI: one scoped instance exposes both library roles.
services.AddEventStore<StockPositionAggregate, StockPositionStore>();

// Native model: registering main state also requires safe rebuilding.
model.ConfigureRequiredInlineState<EventStream, StoredEvent, StockPositionCurrentRow>(family);
```

Reviewed new public surface in the existing EF adapter:

```csharp
public static class EventStoreServiceCollectionExtensions
{
    public static IServiceCollection AddEventStore<TAggregate, TStore>(
        this IServiceCollection services)
        where TAggregate : class
        where TStore : class, IEventStore<TAggregate>, IInlineProjectionRebuilder<TAggregate>;
}
```

This helper registers the concrete scoped store and aliases both interfaces to that instance.
It supplies TimeProvider.System only if the consumer has not supplied a clock. Repeat identical
registration is idempotent; an incompatible existing aggregate binding is a configuration error,
rather than silently selecting a different store/rebuilder. Explicit constructors continue to
select InventoryDbContext/PurchasingDbContext; the helper never resolves a bare DbContext.
Gate/replay currently are constructed by each typed store, so registering providers separately
or discovering hypothetical future services would add machinery without a current obligation.
Module codec/history setup and business entry points remain module-owned.

Main inline-state registration includes the rebuild-enabled model contract. Every provided
registered aggregate store must configure replay and a compatible gate before its first write.
Secondary views remain explicit; raw history-only streams remain permitted. The existing explicit
ConfigureInlineProjectionRebuilding method remains idempotent for compatibility, while samples
no longer repeat it after main registration. This default changes write admission and supported
isolation requirements, not just syntax: participating writers must obtain pre-read admission and
the selected Postgres adapter requires ReadCommitted. Native save guards still require explicit
installation. EF metadata cannot infer domain evolution, durable decoding or the provider.

IAggregateRebuilder<TAggregate> now supplies out-of-the-box technical rebuilding.
IStockPositionRebuilding/IPurchaseOrderRebuilding are optional consumer boundaries: external
hosts cannot name the modules' internal aggregates, and their public Contracts remain independent
of technical library types. Inside a module or ordinary application, call the generic rebuilder
directly. Keep these two facades for the existing external demo host; do not add a public domain
interface, aggregate-discovery catalog or global maintenance framework to the library.

The guarantees are identical scoped role instances, explicit module context selection,
early rejection of incompatible registrations or missing rebuild configuration, and the existing
online repair safety. This proposal does not install authorization, open transactions, save or
commit. DI setup needs an explicit Microsoft.Extensions.DependencyInjection.Abstractions reference
in the EF project, centrally pinned to the repository's selected Extensions version (10.0.12).
No umbrella/provider/codec dependency is introduced into the core or T1.

Exact refinement file/behavior map:

| Files | Change |
| --- | --- |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStoreServiceCollectionExtensions.cs` (new) | Scoped concrete/role registration, default clock, idempotence and binding errors. |
| Same project's `Gloaming.EventSourcing.EntityFrameworkCore.csproj`; `Directory.Packages.props` | Explicit DI abstractions dependency and central version pin. |
| Same project's `RequiredInlineStateExtensions.cs`, `EventStore.cs` | Main registration enables rebuilding; validate gate/replay before writes; retain explicit helper compatibility and raw stream behavior. |
| `samples/Wholesale/modules/{Inventory/Inventory/InventoryRegistration.cs,Purchasing/Purchasing/PurchasingRegistration.cs}` | Replace repeated technical store aliases/default clock with the aggregate helper; retain native history and module Contract registrations. |
| Both modules' `{StockPositions,PurchaseOrders}/InlineViewMapping.cs` | Remove redundant explicit rebuild opt-in after main registration; preserve table/key/concurrency/JSONB mapping and secondary view declarations. |
| `src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/{RegistrationTests.cs,GuardTests.cs,RebuildConsumer.cs}` | Prove same scoped instance, different scopes, compatible repeats/conflicting bindings, clock override, typed-context isolation and mandatory registered-store configuration. Update standalone setup to demonstrate the default; preserve raw adoption. |
| EF/family/Postgres READMEs and local capability records; design/ADR 0007; ES2 brief/report; architecture dependency checks | Document default requirements and optional module facades, update allow-list for explicit DI dependency and record new verification separately. |

Existing history readers, seed StageAsync, module maintenance Contracts, reducers, fixtures,
migrations and archive need no replacement for this refinement. Existing concurrency,
transaction and independent-adoption proofs remain. New execution results are recorded in the
[slice report](../reports/es2-single-stream-rebuilding.md#owner-review-follow-up-2026-10-07).

Historical schema reading/upcasting is a priority for the next capability proposal after ES2
review/refinement. Start from actual retained old event fixtures and explicit deterministic
read-time transformations into current domain facts, preserve stored payloads, and reject missing
paths/future versions. The optional Events.Serialization registry is the natural candidate;
independent direct-JSON stores remain valid. Rebuilding consumes upgraded facts through replay;
projection-meaning changes need a rollout plan, while shape-only upgrades need not force rebuilding.
No upcaster, event v2 fixture, background job or broader rebuilding capability is authorized here.
The [library-local catalog](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md)
retains this priority and proof context beside the package.

The next slice has an owner-approved [payload-upcasting scope](event-payload-upcasting.md)
against checkpoint `91542a6`: one JSON transformation abstraction, optional explicit paths in
the existing codec, a real Inventory schema transition and an independent chained/nested
adopter. Its public interface, errors, ownership, exact file map and verification obligations
were reviewed before implementation, including the provided PassThrough helper for compatible
optional-field additions. This slice does not add a store/history interface,
rewrite historical payloads or authorize maintenance workers.

## ES2 lane separation follow-up (superseded proposal)

Latest review proposal: [aggregate-only reduction](es2-aggregate-only-reduction.md). It gives
the narrowed public surface, explicit Purchasing replacement, retained guarantees and exact
file map. The discussion below preserves how the direction evolved; no broader projection,
catch-up or worker interface follows from it. C# removal/replacement awaits owner review.

Status: design follow-up for owner review, 2026-10-07. The owner initially requested both
explicit delta catch-up and full replay, and endorsed separating normal append from maintenance.
The subsequent priority discussion recommends deferring catch-up for the always-current
inline contract; the earlier two-operation proposal below is retained as design history.
Only full replay is currently implemented. No new public catch-up interface is approved.

Keep normal reads free of repair writes and normal GetForWritingAsync strict about required
inline state. Main and required secondary projections should already represent the observed
head under the supported transactional append contract. Unexpected lag is an integrity
failure to investigate, rather than a reason to silently repair every load.

The recommended maintenance operations both stage projection updates for the consumer's
explicit native SaveChanges/commit:

| Operation | Assumption and behavior |
| --- | --- |
| Explicit catch-up | Trust each existing readable projection at its own saved version; apply only its missing event suffix through one captured stream head. A current row needs no evolution. Missing, unreadable or ahead state fails; use full replay for missing/unreadable state. A wrong body already at the head cannot be diagnosed or repaired by this operation. |
| Explicit full rebuild | Ignore old projection bodies and reconstruct all required views from the complete captured history. Repair wrong same-version, missing or unreadable state; retain the existing ahead-state rejection. |

Both operations preserve events/header metadata, prepare every candidate before changing
tracking, and use the exclusive maintenance admission for persisted updates. Keep them
explicit and terminal in a fresh context. Do not combine command execution with maintenance
in that context. Catch-up must establish an effect-free aggregate at the captured head,
without turning historical facts into pending command events. Each secondary reducer uses
its own saved version, rather than assuming all damaged rows share one offset.

An in-memory catch-up primitive can later support an explicitly chosen checkpoint-plus-tail
write loader. It is not sufficient to change only the main root today: other required views
must also catch up, the existing save guard requires each original projection version to
equal the original stream head, and a decision producing no events would not persist the
repair. Do not weaken that guard or introduce automatic repair until a real consumer needs
this different loading contract. Pure consumer evolution remains reusable independently.

Online and offline maintenance need no separate algorithms or mode flag. Retain the existing
tested shared-before-load writer admission and exclusive maintenance admission. Consumers
may run the same maintenance operation while writers continue, or during an operational
pause. An offline pause must also drain writers that already loaded state. A repair-only
row lock cannot make those old observations safe when repair preserves the event version.
The current PostgreSQL advisory lock excludes cooperating writes to the logical stream;
it does not fence arbitrary external SQL. Safe online maintenance is a selected maintenance
guarantee, not a prerequisite for basic event appending.

The immediate implementation simplification should preserve the reviewed public surface
while changing internal ownership:

| EF adapter files | Proposed internal change |
| --- | --- |
| EventStore.cs; dedicated internal maintenance implementation (new) | Move full replay orchestration out of the append implementation. Retain a small forwarding method for the existing rebuilder interface/registration, and share configured projection bindings without introducing a public configuration framework. |
| InlineProjectionStorage.cs; dedicated internal maintenance preparation (new) | Share native row lookup/key mapping. Separate append preparation from replacement preparation and validation; remove rebuilding booleans and repair-only fields from normal prepared append rows. Preserve all-candidate preparation before tracking and native original concurrency values. |
| EventStreamRebuildState.cs; EventStreamWriteGate.cs; RequiredInlineStateExtensions.cs | Separate writer admission bookkeeping from the exact private prepared maintenance save association. Keep the ordinary append validator and the narrow maintenance validator explicit; preserve provider coordination and consumer save ownership. |
| Existing family PostgreSQL/consumer tests and local documentation | Preserve existing append/online repair proofs; update ownership explanations. Add catch-up proofs only with a reviewed concrete public/adapter proposal. |

Catch-up needs a reviewed consumer evolution adapter as well as an operation name: the
current aggregate contract exposes pending facts and versions, not a generic historical
Apply method. Do not infer evolution through reflection or require public aggregate internals.
Its exact public types, errors and consumer usage remain to be presented before C# changes.
No new provider dependency, schema migration, fixtures, archive change, scheduler, lease,
discovery engine or revision cutover belongs to this simplification.

Maintenance remains useful after a reducer correction, a changed projection definition,
or deliberate restoration/import of retained history without its derived views. Adding a
projection also needs backfill; discovery and rollout are separate deferred obligations.
Transactions and save validation establish atomic participation and metadata consistency,
not the semantic correctness of consumer-derived state. Shape-only event upgrades do not
automatically require rebuilding; changes to derived meaning may do so.

### Model clarification and worker direction under review

The owner's subsequent model discussion considers concentrating active support on one
mandatory inline aggregate state, using native EF query projections for ordinary read DTOs,
and deferring additional persisted and live projection machinery. This is not approval to
remove the existing Purchasing secondary view or its library support. That view participates
in queries, append validation, seeds, migrations and existing demonstrations; any replacement
must preserve those artifacts and have an explicit reviewed file/behavior scope.

Inline means applying newly appended facts during their transaction. It does not mean
automatic history backfill on reads or registration. Live means reconstructing in memory
on demand. For the same reducer, definition and captured event prefix, reconstruction and
persisted inline state should agree semantically, although they are different object instances.
Different read models deliberately have different shapes. Unexpected disagreement at the
same version is an integrity or evolution problem; observing different versions alone is
not evidence of corruption.

Marten's [projection lifecycles](https://martendb.io/events/projections/) distinguish inline,
live and asynchronous execution. Its [FetchLatest behavior](https://martendb.io/events/projections/read-aggregates)
loads inline state directly, rebuilds live state from events, and advances an asynchronous
snapshot in memory from its missing event suffix. That last behavior provides the useful
checkpoint-plus-tail reference; it is not implicit repair of an always-current inline row.
Our ordinary store remains strict. Adding a persisted view for existing streams requires
backfill and a rollout policy before claiming that it is current for all those streams.

Marten [rebuilds persisted projections through its daemon](https://martendb.io/events/projections/rebuilding)
and documents side-by-side versioned projections for online deployment. Its
[Solo daemon mode](https://martendb.io/events/projections/async-daemon.html) assumes one process;
HotCold adds leadership coordination. Useful directions here are separating maintenance
hosting from replay and starting with an explicit single-worker deployment contract. These
references do not establish a portable locking recipe or mixed-reducer deployment support
for our existing per-stream repair.

A queued single-stream rebuild worker is a candidate follow-up after the maintenance lane
is simplified. A minimal durable request identifies the owning family/tenant and stream;
processing opens a fresh owning context/scope, invokes explicit rebuild, saves/commits and
records completion. A crash after repair commits but before completion is recorded can
cause replay again. Define at-least-once processing and effect-free replay, failed requests,
retry and restart behavior before claiming durable support. One worker does not require
a distributed lease engine; the stream gate alone does not provide queue ownership.
The library may supply reusable execution mechanics while the consumer supplies hosting,
tenant admission, typed context and native transaction/save ownership. No job table,
BackgroundService, discovery loop or worker dependency is implemented or approved here.

Live and asynchronous multi-stream views remain deferred. Their source grouping, ordering
and progress contract is different from this aggregate's single-stream version. EF Select
over inline aggregate state can supply ordinary read shapes now, but cannot recreate
historical information deliberately absent from that state.

### Native query assessment and revised work order

The owner clarified that the main command aggregate is inline, endorsed explicit maintenance
backfill, and prioritized inline/multi-stream view design before a maintenance worker. Assess
native EF queries first; if they meet actual view needs, defer a projection engine until after
the first library extraction set, while retaining multi-stream projection work before claiming
the projection capabilities are complete. This does not authorize replacing existing views.

DDD aggregate write boundaries do not prohibit joins in read queries. EF can translate
joins and supported grouping/aggregate forms over mapped state in one owning query context;
see [Microsoft's query operator documentation](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)
and [query shaping guidance](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying).
The concrete current Inventory query is StockPositionQueries.ReadAvailableAsync: it joins
owned inline state to the header on the complete owner/stream key and filters JSONB onHand
before materialization. Its existing PostgreSQL proof was rerun on 2026-10-07: one test passed,
zero failed/skipped. It verifies results, tenant isolation and absence of event-history reads.
This is renewed evidence for the existing native query, not a new multi-aggregate proof,
query-plan benchmark, projection engine or library mechanism.

| View need | Native-query assessment and boundary |
| --- | --- |
| Current DTOs and filters over one aggregate | Native EF selection/filtering over inline state is already exercised. No new event-derived view is necessary merely to shape a DTO. |
| Join or totals over several aggregates owned by one module | Feasible using one native context and supported mapped expressions. Prove the concrete join/grouping, tenant scope and paging on PostgreSQL before claiming its support; current availability/header proof is narrower. |
| Purchasing's existing current summary | Code/currency/line count/total are derivable from PurchaseOrderState. Replacing its independently maintained row with query-time derivation is a candidate, not an approved replacement; server translation of the current JSON line collection and preservation of demonstrations remain obligations. |
| Inventory/Purchasing combined result | Existing internal rows and separate module Contracts/contexts do not expose a shared query provider. Use bounded separate Contract queries and application composition, or review an explicit reporting read model/context. Do not expose peer rows/IQueryable or bypass module ownership for a convenient join. Separate reads do not establish one common database snapshot. |
| Historical metrics absent from current state | Native current-state joins cannot recover discarded history. This can justify a concrete event-derived projection. |

The proposed order is query assessment and concrete view selection, a bounded projection
proposal only where native reads are insufficient, then worker/backfill orchestration that
covers the supported projection shapes. The existing repair primitive and pending internal
lane separation remain available; defer adding a job table/worker rather than discard ES2.
Keep reconstruction/replacement bookkeeping out of ordinary loading/appending. Existing
secondary inline support is retained pending an exact replacement decision.

Multi-stream inline projection maintenance will need its own state identity and concurrency
contract: two writers for different streams may update the same projection row. That row's
progress cannot be treated as one stream version. Cross-module inline projection writes also
require an explicitly reviewed transaction/ownership model; they do not follow from same-module
multi-stream support or from native read joins. A queue around today's single-stream rebuilder
does not supply those guarantees.

The current catch-up recommendation is defer: registered aggregate state and required inline
views advance atomically with events; ordinary fetches never repair them. Missing/behind or
wrong state is handled through explicit full rebuild/backfill. Normal load validation rejects
missing/version-inconsistent state, but cannot identify every semantically wrong body at the
correct version. Revisit in-memory suffix evolution with a concrete snapshot/async projection
contract; it remains distinct from saving repaired state and from multi-stream progress.
This supersedes the earlier recommendation to add persisted catch-up to the current ES2 scope.

The native query rerun proves no new reusable mechanism. Other earlier proof totals are
historical executions and were not rerun in this discussion. Existing staged changes are
preserved; this design follow-up is unstaged. The retained SQL diagnostic remains design
evidence until its unique unsafe counterexample has equivalent family test coverage.

## Checkpoint history

Status: review proposal, 2026-10-03. The owner approved the archive-and-plan direction.
This document proposes implementation increments; it does not freeze public interfaces,
package boundaries, or authorize a commit. E1 was reviewed and checkpointed as `a8e45c9`;
[the checkpoint report](../reports/e1-tenant-actor.md) records that original combined design.
The independent split was reviewed and checkpointed as `c8cbf64`;
[its current report](../reports/e1-identity-split.md) records fresh proofs.
E2.1 was [reviewed and checkpointed as `50e7933`](../reports/e2-1-tenant-ownership.md).
[E2.2 module-owned migrations](e2-2-module-migrations.md) were checkpointed as `f2dcf2b`.
[E2.3 tenant relationships](e2-3-tenant-relationships.md) were checkpointed as `d67c7fc`.
[E2.4 versioned profile changes](e2-4-versioned-profile-changes.md) were checkpointed as
`6069c05`, completing the initial supported E2 scope. Shared cross-module transactions and
[the remaining E2 limits](e2-persistence.md) require separate evidence.
[E3.1 actor HTTP integration](e3-1-http-actor-identity.md) was owner-reviewed and checkpointed
as `faefc0b`; [its report](../reports/e3-1-http-actor-identity.md) records cookie/policy/
claim-action proofs and the registration refinement.
[E3.2 tenancy HTTP integration](e3-2-http-tenancy.md) was owner-reviewed and checkpointed as
`cfbac9a`; [its report](../reports/e3-2-http-tenancy.md) records fresh adapter,
Organization/Inventory consumer and independence proofs.
[E3.3 persisted Access lookup/admission](e3-3-persisted-access.md) was owner-reviewed and
checkpointed as `20a02be`; [its report](../reports/e3-3-persisted-access.md)
records actual PostgreSQL/HTTP consumer evidence.
[E3.4 persisted business ingress and module projects](e3-4-persisted-business-ingress.md)
was owner-reviewed and checkpointed with E3.5 as `31c7a8b`;
[its report](../reports/e3-4-persisted-business-ingress.md) records actual integration proofs. Read
[the approved design posture](../design.md) alongside this plan.
[E3.5 Sales profile mutation](e3-5-profile-mutation.md) was owner-reviewed and checkpointed as `31c7a8b`;
[its report](../reports/e3-5-profile-mutation.md) records native antiforgery, concurrency and rollback proofs.
[E3.6 runtime composition](e3-6-runtime-composition.md) was owner-reviewed and checkpointed
as `28ee797`; [its report](../reports/e3-6-runtime-composition.md) distinguishes
runtime and native exporter proofs from manual dashboard observations.
[E3.7 real OIDC/browser journey](e3-7-oidc-browser-journey.md) was owner-reviewed and
checkpointed as `dc3ac3b`; [its report](../reports/e3-7-oidc-browser-journey.md) records optional
local Keycloak, explicit account mappings and actual Chromium journeys.
[E4 event serialization](e4-event-serialization.md) was owner-reviewed and checkpointed as
`2a49ef3b`; [its report](../reports/e4-event-serialization.md) records fresh two-family codec,
compatibility and independent-adoption proofs. [E5.1 history/hydration](e5-1-event-history.md)
was owner-reviewed and checkpointed as `4cc12a1`; [its report](../reports/e5-1-event-history.md)
records selected-range integrity and two-family reconstruction proofs. Its package division
remains for review alongside the now-implemented [E5.2.1 native EF consumers](e5-2-1-native-event-history.md).
[Their report](../reports/e5-2-1-native-event-history.md) records new PostgreSQL evidence. E5.2.1
was owner-reviewed and checkpointed as `4f4d5b2`. [E5.2.2](e5-2-2-native-event-append.md) is
owner-reviewed and checkpointed with E5.3 as `abcd370`; [its report](../reports/e5-2-2-native-event-append.md)
records new native append evidence and no new library mechanism.
[E5.3 explicit storage registration](e5-3-event-storage-registration.md) is implemented before
E6 and checkpointed as `abcd370`. [Its report](../reports/e5-3-event-storage-registration.md) records
the extracted stream/envelope model utility independently of append orchestration.
[E6.1](e6-1-inline-decision-state.md) was checkpointed as `1ae13d4`;
[its report](../reports/e6-1-inline-decision-state.md) records inline decision state, atomic required
views and no new reusable mechanism.
[T1](t1-template-rehearsal.md) was owner-approved and checkpointed as `8ccf4c8` after review
corrections; [its report](../reports/t1-template-rehearsal.md) records creation and adoption
proofs, with no new reusable runtime mechanism. The ES1 interface/scope was then owner-approved
on 2026-10-06; the final owner-reviewed store/envelope refinement was checkpointed as `ab85ec9`.
The family relocation was then approved and checkpointed as `39c1ab3`. ES2 then received owner interface/scope review and implementation authorization.
No earlier approval or implementation authorization approves a later commit.

## Delivery model

Preserve the old backend and documentation as an executable reference, then deliberately
reimplement one justified mechanism at a time. Introduce its real sample usage immediately;
avoid a library-only phase followed by a large sample integration. The wholesale domain may
be reused, with freshly documented module responsibilities as behavior is added.

An implementation increment delivers the public interface, meaningful mechanism, focused
interface tests, executable sample integration, relevant failure proofs, and documentation.
Add a template recipe or file only when a new consumer setup pattern is exercised. The sample
can supply the concrete source for the final template; it is not a third implementation.
Sample-only capability proofs and adapter increments may introduce no new library.

### Checkpointed actor identity and tenancy

Before implementing E2, the owner authorized revising E1 into independently adoptable
`Gloaming.ActorIdentity` and `Gloaming.Tenancy` libraries, with no dependency
between them. The split was owner-reviewed and checkpointed as `c8cbf64`; see
[the E1 plan](e1-tenant-actor.md).
Actor means the identity performing an operation, not an actor-model execution component.
Executing actor and optional initiator stay together; tenant selection and tenantless
execution belong to the separate tenancy context. The checkpointed combined implementation
is historical evidence at `a8e45c9`. Each holder is single-assignment; combined composition
and completion of required establishment belong to the host.

The tenancy core accepts an explicitly established tenant without prescribing a URL shape.
For E3, propose optional HTTP utilities for configurable route values and hostnames, plus a
consumer resolver for other strategies such as one tenant per application user. Candidate
selection, canonical identity resolution and admission are distinct responsibilities.
Organization remains the sample's domain/UI term; Tenant denotes the technical isolation
boundary. Membership is a separate consumer-owned Access increment in E3, not a prerequisite
or dependency of either foundation library.

### Extraction and strategy gate

The owner confirmed standalone library adoption as the current strategy: a segment can be
used in an ordinary .NET API or worker without the template's module structure or Access
model. This can be revisited if actual implementation shows value in more involved libraries.
Until reviewed otherwise, standalone consumer proofs remain part of the relevant increments.

On 2026-10-15, with C1 approval, the owner reaffirmed incremental extraction from
bounded sample slices followed by feedback from real-project bootstrap and usage.
Simple common mechanics can earn a library; complexity is not a prerequisite.
Compare the consumer work removed with adoption, opt-out, compatibility and eventual
NuGet release/update/support costs. A slice can finish as consumer patterns without
a new package. Real adoption can justify further extraction or revisions; it does
not automatically authorize speculative capabilities or a fixed package inventory.

Access begins as customizable sample/template code. Its presence in the template makes later
extraction possible when reuse earns it; a reusable Access module is not required for E1.
Apply the same evidence-based assessment to other candidate mechanisms rather than treating
the candidate inventory as a promise that each entry becomes a package.

Before promoting template behavior into a library or changing the adoption strategy, present:

1. Concrete consumers or use cases that repeat the behavior, or implementation evidence of
   meaningful complexity removed by the proposed integration.
2. Which behavior is a reusable mechanism and which product policies would travel with it.
3. Consumer usage and customization examples, dependencies and ownership changes, and the
   effect on standalone adoption and existing compositions.
4. Public-interface and end-to-end proof obligations, compatibility responsibilities and
   supported limits. A more involved implementation does not automatically inherit the old
   guarantees or supersede explicit-control decisions.

Review that proposal before promotion. Access extraction may produce an optional feature
module with explicit policies rather than a technical foundation; do not move its membership
or role model into the tenant/actor seam to make extraction convenient. New evidence and a
reviewed strategy change can reorder later increments; neither is silently assumed.

### Current layout and extension direction

- `src/Gloaming.{Family}/`: family README/docs, independently selectable package
  directories and library tests. ActorIdentity, Tenancy, Persistence, Events and EventSourcing
  are grouped without adding runtime dependencies.
- `samples/Wholesale/`: API composition root, modules/Contracts, finite Migrator, AppHost,
  host ServiceDefaults, and sample-specific tests, introduced as needed.
- `tests/`: repository-wide architecture checks and shared test support; library-interface
  and independence tests live in the owning family's `tests/` directory.
- `templates/`: exercised consumer setup, consolidated after working sample usage.
- [Historical Git source](../reports/s1-archive-retirement.md): original source, tests, fixtures
  and dated documents, retired from the active checkout after selected S1 proof transfer.
- `docs/`: current decisions, development commands, reviewed slice designs and proof reports.

The active root solution contains current projects; original solution/build/test policy remains
historical Git evidence. Active references use current owned sources. Library naming and exact
package splits are reviewed within the first increment that needs them; no empty projects are
added to reserve names.

## Candidate inventory

Candidates are proposals supported by archived implementations, not already-proven library
interfaces. Links below deliberately point to the historical evidence.

| Capability | Reusable candidate | Consumer-owned part and decision |
| --- | --- | --- |
| Actor identity and tenancy | Independently adoptable identity and tenant-choice contexts, each with explicit scope validation | Membership, roles, invitation lifecycle, tenant admission, actor trust and authorization remain consumer policy. This is an agreed early library direction. |
| Module persistence | Explicit EF ownership-filter/model utilities and justified write validation | Module DbContext, schema, mappings, migrations and transaction ownership. The archived `shared/Persistence` utility is a starting comparison, not a required base context. |
| Event identity and codec | Explicit alias/version registry, payload encoding/decoding and compatibility errors | Event definitions, required/optional fields, allowed schemas and evolution. Compare both serializers before designing a common interface. |
| Event history | Contiguous ordered-range checks, captured-head reads and deterministic hydration mechanics | Domain reducer, state shape and temporal meaning. Preserve application append time versus commit time. |
| Event-sourcing model registration | Explicit EF stream/envelope mapping, version token, selected keys/relationship and position uniqueness; E5.3 owner-reviewed implementation | Consumer row types, ownership/filter choice, DbContext/provider, migrations, stream families and saves. No required tenancy or codec dependency. |
| Event append | Stream expected-version checks and event/envelope staging in native EF transactions | Business-key identity, decision state, view definitions, audit and transaction owner. PostgreSQL guarantees stay explicit. |
| Inline projections and repair | Explicit batch coordination and bounded reconstruction helpers where genuinely shared | View identities/reducers, required-view policy, write admission, lock granularity and privileged recovery. Purchasing repair is not yet proven. |
| Reliable messaging | Inbox/outbox storage, lease claims, token-guarded completion/backoff and callable dispatch | Semantic operation identities, fingerprint meaning, producer trust, retention, routes and payload mapping. Delivery deduplication is not business idempotency. |
| Rebus/RabbitMQ integration | Optional adapters around demonstrated delivery/publishing behavior | Consumer creates endpoints and wires queues, topics, subscriptions, handlers, routing and retry/error policy. No generic bus registration facade. |
| Audit | Common technical envelope and explicit staging, if comparison earns a library | Action/reason vocabulary, denial policy, sensitive details, retention and query visibility. Event streams are not security audits. |
| Authentication and Access | Evaluate focused technical utilities and, later, an optional Access feature module if reuse earns it | BFF/OIDC settings, principal completion, users, Organizations, memberships, invitations and roles begin as customizable sample/template code. Promotion follows the extraction and strategy gate; Access is not a technical-library foundation. |
| Saga/process coordination | WF2 supplies optional explicit typed continuations and provided EF process storage after owner review | Ordinary consumer methods own branching, compensation/effects and business replies. Library matches operations, continues saved methods and enqueues requests. Native transaction/admission/transport remain explicit; no replay/graph/scheduler engine. |
| DDD and CQRS | Small pending-event or value utilities only if they remove meaningful duplication | Rich aggregates, children, value-object rules, domain services, handlers/results and direct queries begin as consumer code. No mandatory inheritance or mediator. |
| Structure, API and registration | Explicit sample conventions and configurable architecture utilities | Consumer-selected graph, HTTP ingress and module composition. A configurable policy tool must demonstrate an alternative layout and detect violations. |
| Testing and delivery | CI lanes, fixtures, architecture checks and development tooling | Extract helpers only when used; template pipeline is not a runtime dependency. Dependabot or an alternative enters with a reviewed update policy. |

Evidence entry points:

- [Tenant context and module collaboration](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/modules/README.md).
- [Second aggregate comparison](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/second-event-sourced-aggregate.md).
- [Event-sourcing gate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/event-sourcing-correctness-gate.md).
- [Messaging reuse comparison](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/messaging-reuse.md).
- [Durability closure](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/durability-closure-checklist.md).

## Dependency and interface review

Actor-identity and tenancy libraries have no dependencies on each other, EF, web, transport,
Aspire or sample Access.
Provider-specific persistence depends on the selected EF/PostgreSQL packages and only the
smaller seams its tested mechanism requires. An event codec need not depend on a database;
event persistence and messaging need not depend on one another. Rebus integration is optional.
Audit and other segments do not acquire unrelated transitive runtime packages for convenience.

Decide whether a separate abstractions project is useful only when actual consumers require
it. Replacing a concrete dependency with a wrapper is insufficient justification. Service Bus
is a future real-adapter proof, not an interface designed solely from RabbitMQ assumptions.
Keep provider-specific capabilities available alongside any later common subset.

Before implementing an increment, present:

1. What complexity the interface removes, backed by concrete archived consumers.
2. A small consumer usage example, required registrations and configuration, and expected errors.
3. Who creates the scope/context/connection, stages changes, saves, commits, retries, publishes
   and disposes; include cancellation and concurrency behavior.
4. Supported options and how each changes the guarantee; distinguish policy replacement from
   ordinary configuration. Schema and route choices cannot carry sample defaults implicitly.
5. Public types, actual dependencies, independent-adoption examples, and test/CI lanes.
6. Archived proof scenarios transferred, known limits retained, and behavior deliberately changed.

The following is an illustrative transaction shape, not a selected public interface:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

// These operations stage into the caller's context and never save or commit.
await events.StageAppendAsync(db, append, cancellationToken);
audit.Stage(db, auditEntry);
outbox.Stage(db, explicitlyMappedMessage); // Only when messaging is opted in.

await db.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

A necessary repair/write guard may be acquired before loading state and held through commit.
A scope helper must document whether it establishes that guard, a transaction, or only context.
Disposal never silently commits. Define or reject nesting, context reuse, and concurrent use.
Unexpected storage faults require a fresh operation scope unless a narrower reuse guarantee is
explicitly demonstrated. This example assumes a single owning-module transaction and makes
no cross-module transaction claim.

## Proposed implementation increments

Every increment leaves a runnable sample or preserves its existing runnable behavior. Each
has library/template/sample findings and the verification described below. Split further if
the interface and failure matrix cannot be reviewed together in one sitting.

### E0 Archive and active documentation

This checkpoint was reviewed and committed as `0690475` on 2026-10-03.

Preserve the original tracked source and its provenance without implementation changes.
Retain minimal active policy, tooling, CI and a proposed extraction plan. Verify checksum
identity, relocated solution/build discovery, formatting, Fast tests and representative
container/host paths. The owner reviews retained/new files and tooling changes rather than
line-by-line relocation. No new reusable mechanism is proven by this increment.

Verification on 2026-10-03: all 800 original tracked files match the snapshot manifest;
the relocated 20-project solution restores and builds with zero warnings. Root/archived
formatting, semantic style, analyzers, active documentation links, YAML and workflow paths
passed. Architecture passed 21 cases, application 27, and the full PostgreSQL lane 163.
Focused broker replica-death checks passed four cases across all three consumer modules;
one Topology case passed two full application lifecycles with migration/health/telemetry
checks. Broker and Topology selections are focused relocation proofs; their complete suites
remain wired into CI. Local restore needed network access for NuGet vulnerability data;
format/test hosts needed local IPC/container access. No audit or assertion was disabled.

### E1 Independent actor identity and tenancy with a minimal sample

The owner reviewed the original combined E1 implementation and authorized checkpoint
`a8e45c9`; [its report](../reports/e1-tenant-actor.md) remains historical evidence. The owner
subsequently authorized the independent split, owner-reviewed and checkpointed as `c8cbf64`; see
[the revised plan](e1-tenant-actor.md) and [current report](../reports/e1-identity-split.md).
New lifecycle/concurrency and independent-adoption proofs are distinct from archive evidence.

Context is immutable within one operation; changing tenant, actor or initiator requires a
separate operation context. Use opaque string keys in distinct tenant/actor value types,
with consumer-owned domain identity mapping. Keys use ordinal comparison, reject empty or
whitespace-only values, and preserve accepted values exactly; canonicalization is consumer
policy. Operations read each selected segment through its injected, read-only accessor. The
host initializes each exactly once per operation scope through its separate initialization
interface, finishing the segments required by a capability before invoking it. Reading
before establishment and any repeated initialization are errors; no reset, replacement or
anonymous fallback is supported. Review explicit registration and lifetime for human and
workflow consumers. Immutable values do not depend on DI. Tenant selection never asserts
membership or permission. System actor identities come from trusted consumer wiring, not
caller input.

Support concurrent reads after initialization within one operation; parallel business work
starts only after establishment completes. Provide small, explicitly called context
requirement checks; their use and failure presentation are consumer choices. Establishing
trust, resolving membership, checking permissions and mapping HTTP failures remain outside
the core seam.

Support explicit tenantless execution separately from missing required context. Keep current
actor and original initiator distinct, including a workflow acting on work initiated by a
human. Initiator is optional, explicitly supplied when known, and neither inferred from the
actor nor automatically propagated. Neither attribution nor context establishment grants
permission; consumer policy owns authorization and the effect of later membership revocation.

Represent anonymous execution explicitly, separately from missing actor context and from
tenantless execution. Consumers choose which capabilities permit anonymous actors. A public
tenant-specific operation and an identified actor's tenantless operation are both valid
compositions when the owning capability permits them.

Provide a runnable sample host and two real capability calls exercising isolated contexts,
with concurrent operations, permitted tenantless work, rejected missing required tenants,
permitted anonymous work, rejected anonymous actors where an identified actor is required,
missing/mismatched context cases and distinct actor/initiator attribution. Test setup actors
are not a production authentication implementation; expose tenant business HTTP operations
only after trusted ingress is in place. Prove exact key comparison and validation, early-read
and repeated-initialization errors, optional attribution, concurrent reads within a scope,
no cross-scope leakage, explicit requirement checks, forbidden scope rebinding and cleanup
on exceptions/cancellation. Build a minimal consumer without EF, ASP.NET Core, Rebus, Aspire
or sample Access.Contracts. Add active solution, Fast lane and hook coverage.

Template output: explicit context establishment and lifetime recipe if this is a new setup
pattern. Domain policy stays in the sample.

Historical comparison: Access uses [GUID-based tenant IDs](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access.Contracts/Organizations/OrganizationId.cs)
and [user IDs](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access.Contracts/Identity/UserId.cs),
while [external identity](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access.Contracts/Identity/ExternalIdentity.cs)
uses issuer/subject strings and [Sales workflow identity](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Sales/Sales/Fulfilment/OrderFulfilmentProcess.cs)
is a name. This does not prove that a reusable context must prescribe GUIDs. Archived
[HTTP admission](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/apps/Api/Modules/Access/Middleware/OrganizationScopeMiddleware.cs)
and [module authorization](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Sales/Sales/Authorization/SalesRequestAuthorization.cs)
are explicit consumer checks; they do not prove a universal library authorization mechanism.

The owner agreed to start the sample/template with a stable, globally unique application
UserId, with consumer-owned external-identity resolution feeding the human actor key.
Authentication-provider details stay outside both foundation libraries; neither requires
provider fields nor an application User entity. Human/system actor kind distinguishes
otherwise equal key text.
In the archive, [identity resolution](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Identity/LinkExternalIdentityHandler.cs)
matches issuer and subject; an unrecognized pair creates a new user. It does not automatically
merge users with matching email addresses. For OIDC, [claim stability rules](https://openid.net/specs/openid-connect-core-1_0.html#ClaimStability)
identify issuer/subject as the stable pair and do not guarantee email stability or uniqueness.
Recommend explicit, verified account linking as consumer Access policy; its implementation
and admission choices belong to a later sample slice, not E1.

### E2 Explicit EF tenant and module persistence utilities

The owner confirmed shared database/module schemas/tenant discriminator, immutable
ownership for ordinary persistence and native development-time migration scaffolding.
Review [the implemented first increment](e2-1-tenant-ownership.md) and
[its fresh proof report](../reports/e2-1-tenant-ownership.md). Explicit EF ownership filters,
tracked-write validation and native ownership predicates passed 61 tests with an Inventory
consumer and an independent GUID consumer. [The complete E2 plan](e2-persistence.md)
and [earlier design findings](../reports/e2-persistence-design.md) distinguish the remaining
scope. Shared cross-module transactions receive a separately
designed workflow/proof rather than being implied by this increment.

Use real sample module DbContexts and two consumer-selected schemas. Review explicit model
registration, tenant-owned rows/indexes and the chosen write-validation mechanism. It must
handle inserts, updates, ownership changes and relevant child relationships through tested
semantics; query filters alone do not prove all write paths are safe. Document raw SQL and
privileged bypass responsibilities. Configure an alternative ownership/schema policy to
prove the utility does not freeze the sample layout.

Prove missing/foreign context, cross-tenant identifiers, model/migration ownership, competing
writes and rollback on PostgreSQL. Keep normal EF construction, mappings, migrations, saves
and commits visible. Do not add an ambient unit of work or cross-module transaction.

Template output: ordinary DbContext/model/migration registration. Add the PostgreSQL CI lane
for active code in this increment; archived lane remains distinct.

### E3 Trusted ingress and state-stored sample path

Start with [E3.1's approved actor-only HTTP adapter scope](e3-1-http-actor-identity.md): effective
native principal mapping, explicit evaluator/middleware composition and focused request
proofs. It was owner-reviewed and checkpointed as `faefc0b`, including explicit registration
helpers and a native cookie/OIDC sample recipe. Remote provider topology is unproven.

[E3.2's tenancy HTTP interface and scope](e3-2-http-tenancy.md) were reviewed and checkpointed
as `cfbac9a`. The owner confirmed
middleware after native authorization and actor establishment, before endpoint work. This
first adapter enforces tenant defaults/metadata and consumer resolution/admission without a
second policy evaluator; tenant-aware native authorization handlers require a later slice.
Optional route/subdomain candidate helpers and independently adoptable tenancy-only proofs
now accompany the extended actor/tenant HTTP sample.
[The report](../reports/e3-2-http-tenancy.md) records actual results. Durable Access is not part of E3.2.

[E3.3 persisted Access lookup/admission](e3-3-persisted-access.md) was checkpointed as
`20a02be`: global user and
external-identity lookup, canonical Organization lookup, current membership admission,
explicit public catalog policy and admission-time revocation semantics on real PostgreSQL.
The owner approved these policies; [the report](../reports/e3-3-persisted-access.md) records
the implementation proofs. Technical libraries are unchanged and no new reusable mechanism
was extracted. Editable Access remains sample/template code.
Inventory remained a fixture in E3.3. The checkpointed
[E3.4 persisted business ingress and module projects](e3-4-persisted-business-ingress.md):
read-only tenant-owned Inventory data using E2, populated Access/Inventory implementation
and Contracts projects, host-owned HTTP bridges, separate histories and explicit setup.
The owner approved this scope; [the report](../reports/e3-4-persisted-business-ingress.md)
records actual reads, migration compatibility and independent-consumer results.
[E3.5 Sales profile mutation](e3-5-profile-mutation.md) is checkpointed: native antiforgery for cookie
JSON mutations, application-actor token binding, explicit native Sales saves/transaction,
expected-version conflicts and rollback after a partial write. The owner selected this
business capability and approved its interface and token/authority policies.
[Its report](../reports/e3-5-profile-mutation.md) records the new HTTP/PostgreSQL path, including controlled precommit cancellation.
These are consumer increments; no new library mechanism is assumed.

Reassess focused native authentication/antiforgery utilities when real ingress exposes a
repeated mechanism. Actor/principal mapping and tenant admission remain editable consumer
adapters. The owner narrowed the antiforgery candidate to an optional human-actor requirement;
the current native token binding/filter/HTTP setup stays editable template code. No utility
was extracted and neither core gains a dependency.
Membership administration, invitation acceptance, explicit account linking and permissions
may also earn an optional Access feature module. Revisit that extraction gate when their
actual workflows and alternative policies exist; it need not wait until E9.

E3.6 runtime composition/telemetry is checkpointed. [E3.7](e3-7-oidc-browser-journey.md)
implements disposable OIDC hosting and real browser login/callback, mapped actor, admission
and protected profile mutation, checkpointed as `dc3ac3b`. This closes the bounded E3 ingress
scope; [E4 durable event identity and payload codec extraction](e4-event-serialization.md)
is checkpointed as `2a49ef3b`, before E5 history/append.
E3.7 adds no technical
library or provider-neutral authentication abstraction. Its local HTTPS topology does not
prove external cross-site providers, proxy/subdomain sessions or account administration.
E3.7 supplied exercised sample recipes. T1 now materializes a separate bounded state-stored
composition; generating this richer HTTP/OIDC composition remains later work.
Native ServiceDefaults remains template source; the owner confirmed it warrants no Foundry
library wrapper.

Build the smallest real Access-to-business-module journey using the new context seam and
persistence utilities. Keep BFF/OIDC, memberships, permissions, Minimal API endpoints and
CQRS code in the sample. Freshly record module ownership and domain language. Exercise two
users/tenants, current membership and antiforgery behavior through real ingress.

Review membership and tenant admission as a separate Access increment: an application user
can belong to multiple Organizations, and admission concerns the Organization selected for
this operation. Prove accepted and denied membership, independent operation scopes for the
same actor in different Organizations, and the consumer's revocation policy. Membership
management, invitations and role/permission behavior receive further increments as needed;
none becomes part of the actor-identity or tenancy core.

This may be several reviewable sample-only increments. A ticket-store or policy utility
becomes a separate library only if its comparison removes meaningful repeated complexity.
The review proposes independently adoptable ASP.NET Core actor and tenancy adapters: explicit
consumer identity/tenant resolution, one initialization per segment, and configurable tenancy defaults with endpoint/group
exceptions. The template defaults to native authentication requirements plus a required
tenant. Native authorization decides caller access, with `AllowAnonymous` working without
an extra actor opt-out. Do not introduce a separate HTTP actor policy or actor-specific
endpoint extension. Propose separate tenantless metadata as the tenant exception. Tenancy
defaults must survive named authorization policies; native named policies own their intended
authentication requirements. Resolve authenticated principals to application actors without
silently downgrading mapping failures to anonymous execution.
Review the resolver and error/selection interfaces with real HTTP proofs, including metadata
precedence, anonymous tenant access, identified tenantless access, authentication schemes,
native challenges, denied admission, pipeline ordering, login/callback/health paths and
independent adoption. Include unmapped authenticated principals, authenticated callers on
anonymous endpoints, and native policies deliberately permitting anonymous access. Resolve
tenant identity/admission before initializing the tenancy context;
do not mutate it later. Provider-specific authentication, membership/admission and HTTP failure
policy stay consumer-owned; generic context establishment can be reusable without embedding
that policy. Introduce an editable template-local native cookie/OIDC registration helper here;
focused authentication library utilities require subsequent reuse evidence. The details and
native-policy references are in [the HTTP integration review](../design.md#scope-and-http-integration-review).
Native host ServiceDefaults/AppHost/Migrator are sample/template composition. Add Topology
coverage with actual identity/session wiring, not a production fake actor. This establishes
that state-stored modules work without event sourcing or messaging.

### E4 Durable event identity and payload codec

[The approved E4 scope](e4-event-serialization.md) compares the two archived serializers
and defines the native JSON codec, standalone two-family consumer, dependency promise and
relevant proofs. `Gloaming.Events.Serialization` was owner-reviewed and checkpointed
as `2a49ef3b`; [the report](../reports/e4-event-serialization.md) records actual
bounded guarantees without claiming event-store behavior.

Compare Inventory and Purchasing stable identities, required/optional payload behavior,
error classification and serializer configuration. Replace their repeated technical logic
through a small library exercised by both new sample event families. Explicit registration
is preferred over discovery that quietly registers additional types.

Use retained literal JSON plus independently expected state/results. Prove unknown and
conflicting identities, unsupported schemas, missing required fields, deliberately optional
fields and CLR rename independence. Do not invent a v2 event or generic upcaster to justify
an interface. The codec consumer builds without EF or messaging. This slice need not add a
new template beyond explicit event registration.

### E5 Event history and native append

Deliver ordered-history/hydration and stream/envelope staging as independently reviewable
increments if necessary. Integrate both aggregate families using the owning module's native
EF transaction. Domain deciders/reducers and state shapes stay consumer-owned.

[E5.1](e5-1-event-history.md) implements metadata-only ordered-range validation and explicit
two-family hydration, owner-reviewed and checkpointed as `4cc12a1`. Consumer queries own
version/time selection;
the initial complete-history snapshot/selection object was rejected during review.
[Its report](../reports/e5-1-event-history.md) separates the new mechanism from consumer policy.
E5.2 is split into two reviewable capabilities:

- [E5.2.1 native EF history reads](e5-2-1-native-event-history.md): integrate both families
  into owning modules, map streams/envelopes with native migrations, exercise tenant-scoped
  version/time queries and bounded captured-head reads on PostgreSQL. Use explicit finite
  setup writes; this is not an append protocol. Owner-reviewed and checkpointed as `4f4d5b2`;
  [the report](../reports/e5-2-1-native-event-history.md) records new proofs.
- [E5.2.2 native append](e5-2-2-native-event-append.md): expected-version staging,
  caller-owned save/commit, competing
  writers, stream/event-write faults, rollback and fresh-context recovery. Owner-reviewed and
  checkpointed with E5.3 as `abcd370`. [The report](../reports/e5-2-2-native-event-append.md) compares the
  consumer-owned writers: native EF supplies the concurrency/transaction mechanism. Reassess
  the repeated staging shape with E6's required participants before proposing another interface.

E5.1 does not establish database capture or transaction guarantees. E5.2.1 proves native
read composition under the documented append-only assumption, adds no new reusable mechanism
and recommends retaining the small independent History library for owner review. E5.2.2 now
proves append atomicity, conflicts and rollback for the explicit module protocol on PostgreSQL.
Its initial E5 read/append scope is implemented; required views, audit and messaging have not
yet participated in these transactions.

Prove captured-head contiguous history, version/time selectors and regression/corruption
classification, competing append, stream/event-write faults, replay with no external effect,
and caller-owned save/commit. Neither API promises projection-independent business-key
uniqueness. An event-sourced consumer runs without messaging; state-stored composition is
unchanged. Add no generic aggregate repository or compulsory DDD base type.

### E5.3 Explicit event-sourcing storage registration

[The E5.3 scope](e5-3-event-storage-registration.md) extracts a narrower capability
before E6: duplicated technical stream/envelope mappings in the two active modules. Implemented
one EventSourcing.EntityFrameworkCore library with native EF Relational, consumer-owned row
interfaces and explicit model registration. A tenant-free overload uses ordinary identities;
native key expressions support the current owned keys. Ownership filters and jsonb mapping
remain explicit consumer configuration; no actor, tenant, codec or messaging dependency.

The owner authorized the interface before implementation. [The report](../reports/e5-3-event-storage-registration.md)
records new adoption proofs: preserved module schemas/migrations and append behavior, multiple
StreamType values in one table pair and customized tenant-free independent usage. The new
mechanism is model registration; it does not enforce append-only behavior or commit transactions.
Implementation was owner-reviewed and checkpointed as `abcd370`. Append coordination remains
a separate candidate evaluated with E6 evidence.

### E6 Required inline views and bounded repair

[E6.1 inline decision state and required views](e6-1-inline-decision-state.md) was checkpointed
as `1ae13d4`. [Its report](../reports/e6-1-inline-decision-state.md) records earlier proofs of ordinary
edits without history reads, complete-batch validation, independent summary evolution and
atomic event/header/view updates on PostgreSQL. No new reusable mechanism was proven: local
immutable candidates suffice, and concrete view checks/staging do not yet justify a generic
projector interface. Explicit Contracts, mappings and orchestration are editable template
recipes; T1 materializes an event-free composition, not these event recipes. Bounded repair
was deferred during ES1. The reviewed [ES2 scope](es2-single-stream-rebuilding.md) selects
single-stream rebuilding only; wider E6.2 capabilities remain deferred.

The broader E6 objectives below remain the gate for subsequent increments.

Before proposing the E6 interface, compare the archived aggregate wrappers, deciders,
candidate-state policies, live readers and inline projectors with the Marten reference.
Record that comparison in [the event-sourcing reference review](../reports/marten-event-sourcing-reference.md).
The E5.3 mapping proof does not settle aggregate loading, projection execution or repair.
Do not infer that these capabilities have no reusable mechanics from native EF providing
the transaction and concurrency primitives.

First establish one reviewable decision-and-inline-view capability in both aggregate families:

- Keep command eligibility and complete-candidate invariant validation separate from historical
  evolution. Accept a batch only after its final candidate is valid; a rejected batch must
  not change accepted state or pending events. Aggregate wrappers remain optional consumer code
  unless their bookkeeping earns a separately reviewed utility.
- Load current decision state from an aggregate-shaped inline write view and compare its
  version with the stream registry. Keep live reconstruction available as an explicit read;
  demonstrate that ordinary editing can succeed without replaying the whole history.
- Apply accepted events to explicitly selected inline views. An independent summary owns its
  reducer and advances from its own committed state. Calculating an in-memory preview, staging
  inline rows, and repairing persisted rows are separate operations.
- Compare repeated expected-version checks, accepted-batch handling and required-projector
  coordination before deciding whether to extract a callable utility. Consumers retain projector
  definitions, persistence mappings, required-view policy and the final save/commit. No assembly
  discovery, generated projection code or SaveChanges interception is proposed.

Integrate an aggregate-shaped write view and an independent summary view with one atomic
append. E6.1 proves rollback of required views/events/header. Audit needs its own participant
and failure proof before event/view/audit atomicity can be claimed. Verify results with
independently expected quantities and totals. Missing/lagging required views fail according
to explicit consumer policy; ordinary append does not silently repair them.

Review Inventory-style repair separately. Keep privileged admission, identity discovery,
lock lifetime and stream-creation coordination visible. Full reconstruction remains bounded
maintenance until measured evidence justifies another mechanism. Purchasing repair requires
its own implementation and proof before it can be claimed. Async projections, checkpoints,
shadow reconstruction and leader election are outside this extraction increment.

Keep async projections as a later capability candidate requiring an actual eventual-consistency
consumer and its own scope. A global event position alone does not establish committed ordering
or safe progress. Explicit callable processing, progress/effect atomicity, gaps, competing workers
and replay without external effects need new proofs before supporting that execution model.
Metadata, checkpoint snapshots, projection revisions and pending-event preview likewise remain
separate candidates, rather than compulsory fields or services in E5.3 storage registration.

### E7 Reliable messaging storage and callable dispatch

Compare Sales, Inventory and Purchasing inbox/outbox shapes. Stage receipts/outgoing work in
the caller's module transaction. Preserve the distinction between delivery identity and the
consumer's semantic operation identity. Review lease claims, token checks, clock source,
retry/backoff policy, retention and identity-preserving redrive.

Separate one callable dispatch operation from the optional BackgroundService loop. The
consumer supplies publication/routing; the worker registers only when selected. PostgreSQL
proofs cover competing claims, expired lease takeover and stale claimant completion. A
consumer runs this segment without event sourcing; event sourcing runs without it.

### E8 Transport integration and durable sample round trip

Add explicitly wired Rebus/RabbitMQ handlers and publishing adapters; the consumer owns
endpoint creation, queue/topic names, subscriptions, routes and error/retry policy. Rebuild
reservation/shortage/release through sample Contracts, preserving stable operation identities.
Sales owns durable process transitions and compensation. Reconstruct before/after-commit,
pre-ACK, duplicate, reordered, poison and competing-replica scenarios from archived evidence.

Review delivery atomicity and ambiguous external outcomes with real PostgreSQL/RabbitMQ.
Keep process-local retry limits explicit. The selected native publication cancellation/
stalled-confirmation and unavailable-collector shutdown now have
[process evidence](../reports/w1-broker-recovery-shutdown.md). This does not establish a
universal publisher/exporter shutdown bound or every deployment's readiness.
Do not hide unsupported transport cancellation behind a token parameter. A generic saga
library remains a later candidate, not the outcome of a working Sales process manager.

### E9 Audit and remaining utility decisions

Compare audit envelopes across human and workflow consumers. Extract technical storage or
staging only if it removes meaningful duplication without automatic auditing. Accepted-change
audit participates in the native module transaction; denial audit has explicit semantics.
Keep payload classification, action/reason identifiers, retention and query policy local.

Reassess DDD utilities, email, authentication, configurable architecture-test helpers and
workflow mechanics individually. Omit candidates that merely rename native APIs or require
large consumer setup to hide little complexity. This increment is a decision gate, not one
large library PR; each selected mechanism receives its own consumer/proof slice.

### E10 Template rehearsal and delivery basis

The first bounded creation/adoption rehearsal was delivered early as T1 (`8ccf4c8`). Its
initial-creation behavior and fixed state-stored composition are supported now. The objectives
below concern further supported compositions and delivery, not a requirement to repeat T1.

Consolidate exercised sample setup into the template and create a second consumer with
selected segments. Verify build, migrations, local startup, independent adoption and CI.
Naming/configuration stays bounded by actual exercised choices. Scan durable aliases/schema/
route names deliberately.

Plan a bootstrap CLI that applies selected template code and configuration to a repository.
Future selections may include RabbitMQ or Service Bus, optional event sourcing, selected
Aspire resources and OIDC/Keycloak setup with directory-gated or open registration. These
are intended configuration axes; implementing every alternative is not required by the
initial extraction. Add supported options as the corresponding implementations and real
consumer compositions are proven. Materialized files remain consumer-owned and reviewable.

Before implementing the CLI, review its supported compositions, input/configuration format,
existing-file conflict behavior and repeat-invocation semantics. Verify each supported
composition and relevant interactions; reject unsupported combinations explicitly. Prove
that omitted capabilities do not leave required packages, workers, resources or registration
behind. Template-time selection preserves explicit runtime wiring and provider-specific
features; it does not require a universal provider interface or runtime code generation.

Add the selected dependency-update policy and, when frontend work starts, a pinned pnpm
workspace with actual shared subpackage usage. OCI packaging, real Service Bus/production
OIDC compatibility, deployment and restore/rollback require separately planned evidence.
Extraction does not inherit the old Azure pilot as a library requirement or certify it.

## Proof transfer and unresolved limits

| Archived finding | Requirement for the new implementation |
| --- | --- |
| Business-key lookup depends on rebuildable views | Preserve the limitation explicitly, or prove a consumer-owned durable identity constraint before claiming uniqueness through projection loss. Never interpret missing view as universally safe creation. |
| Inventory repair uses an Organization-wide cooperative gate | Retain the tested coordination scope, or prove a replacement covering existing writers, new streams and lock ordering. Native advisory locks do not constrain arbitrary bypass SQL. |
| Purchasing has no projection repair | Live reconstruction is a read, not repair. Add and prove a real owning-module repair before supporting deleted-view recovery. |
| Retained reservation children increase replay/append cost | Keep state shape outside the library. Measure real payload/state mixes; event count alone is not a capacity guarantee. |
| Stream, views, audit and workflow receipt/outbox share a native transaction | Repeat failure of each required participant through the new consumer wiring. No staging method independently commits or publishes. |
| Delivery can repeat and external outcomes can be ambiguous | Preserve semantic identities and idempotent effects; timeout or absent response does not establish business failure. |
| Technical retries are process-local | Do not advertise a global attempt cap. A different policy requires competing-replica and restart evidence. |
| Stalled publish and exporter shutdown need actual fault evidence | The [selected native process continuation](../reports/w1-broker-recovery-shutdown.md) exercises RabbitMQ resource-alarm/SIGTERM and separately budgets the existing unavailable-collector stops. Native settings and fifteen-second verification budgets are composition-specific; arbitrary publisher/exporter deadlines and general deployment readiness remain unsupported. |
| Shared cross-module transactions were deferred | Add a named workflow and real shared-connection/enlistment/failure proof as a separate increment if required. |
| Snapshot-plus-tail has one concrete late consumer | Keep bootstrap ordering/checkpoint policy in the sample until another real consumer earns a library. |

Archive test counts and measurements remain historical. New test runs must identify the
new interface and consumer configuration they actually exercise. Keep retained fixtures and
independent semantic expectations; avoid implementation-equivalence-only assertions.

## Review and completion

For each increment, review public types and consumer obligations first, then implementation,
consumer wiring, tests, and documented limits. Report files worth line-by-line review and the
complexity removed. All changes remain unstaged; every commit needs exact-change-set approval.

Completion of extraction requires a working sample using the libraries through project
references, passing capability-specific proofs, tested independent adoption, and exercised
template creation. Candidate inventory and matching archived results alone do not complete
that goal. Only guarantees exercised by the active implementation and documented in its
report count as new reusable proofs; later increments remain proposals.


### B2 canonical adoption: outbox route claim selection

Implemented under the current owner preapproval after canonical Wholesale exposed real duplicate
claim-routing burden. `OutboxDispatchOptions.RouteKeys` is an optional init-only
`IReadOnlyCollection<string>?`; null preserves all-route behavior. The PostgreSQL dispatcher
copies it at construction and uses exact C-collated parameterized route selection before native
locking. Empty/blank/null entries fail validation before I/O. There are no new dependencies,
transport interfaces or sample policy in the library. Route assignment remains consumer-owned;
this does not authorize tenants or bypass RLS. See the self-contained
[family contract](../../src/Gloaming.Messaging/docs/route-selection.md) and
[product adoption evidence](../reports/b2-directory-product-adoption.md). Existing S1/B2 reports'
initial no-extraction findings describe their earlier checkpoints; this later composition supplied
the first concrete need and new provider/consumer proofs.
