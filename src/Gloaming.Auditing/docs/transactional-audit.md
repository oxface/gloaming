# Transactional audit contract

[Package setup and usage](../Gloaming.Auditing.EntityFrameworkCore/README.md) is independently
adoptable. [The reviewed E9 scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e9-explicit-transactional-audit.md)
records the comparison that earned this extraction.

## Native lifecycle

1. Consumer creates the scope/context, trusts attribution, authorizes work and begins its
   native EF transaction. An inbox processor may already own that transaction.
2. Consumer explicitly constructs AuditEntry, directly or through its own contextual wrapper. Validation and JSON cloning finish before
   Stage tracks a provided AuditRecord. No clock, identity, tenant or classification fallback.
3. Stage associates that row object with its exact context lease and native transaction.
   Both save overrides explicitly validate added row/envelope/transaction association.
   Modified/deleted tracked audits are rejected. No library SaveChanges interceptor exists.
4. Consumer saves once or several times, then commits explicitly. Disposal never commits.
   A failed participant or cancellation rolls back business effects and audit; recovery uses
   a fresh operation/context. Consumer code owns retry and ambiguous commit reconciliation.

EF supplies atomicity within one owning transaction. Validation strengthens the supported
tracked-write protocol; it does not authorize arbitrary SQL or enforce all product policy.
Do not use a DbContext concurrently, rebind actor/tenant within an operation, or nest/replace
the transaction around staged entries. Sharing a connection across modules needs separate
proof and is not supported by this slice.

## Consumer-owned selection and policy

Sales explicitly audits accepted profile version changes, requires an established Human actor
and excludes name/address content. Its scoped SalesAudit wrapper gathers established actor/tenant,
clock and UUIDv7. Its native transaction contains both customer and address changes plus audit. HTTP authentication, admission and antiforgery stay native consumer
wiring; direct calls still need caller-owned authorization.

Inventory explicitly audits accepted inbox stock issues under its receiver-selected System
actor. Its incoming wire contract has no trusted initiator, so initiator remains absent.
Its scoped InventoryAudit wrapper gathers established actor/tenant, clock and UUIDv7; the
business call supplies stock identity, version and quantity. It needs no message or Activity
context.
The processor's transaction covers header/facts/inline state/audit/reply/completion.
Refusal replies, duplicate delivery, ordinary direct commands, seeds and replay do not gain
automatic accepted-change auditing.

The standalone proof consumer chooses a custom schema/table/ID column, tenantless attribution,
optional initiator and plain business rows. It creates disposable proof databases with native
EnsureCreated; the module consumers use reviewed forward migrations.

SubjectKey is the optional item/aggregate identity; null allows a global/subject-wide event.
ConfigureAudit supplies the native tenant/type/item/time/ID index by default as
ix_audit_subject_timeline. Consumers can rename or
replace it with native EF configuration.
Read permission and tenant visibility remain consumer policy; there is no timeline service.
The optional PostgreSQL package maps JSONB; generic relational mapping requires an explicit
provider mapping. Both return editable native builders. Named V1 payload records keep schema
version selection in each wrapper, beside the payload fields; callers provide business values.

The finite broker journey displays the committed action, actor and reply identity. Audit
contains no correlation, causation or trace IDs. Existing messaging metadata remains in its
wire/inbox/outbox envelopes; tracing propagation/export are separate capabilities.

Consumers decide which changes must be audited, permitted actors, action/outcome/reason names,
sensitive fields, detail evolution, query access, indexes, database grants and retention.
Tracked delete rejection means privileged retention needs an explicitly authorized native
maintenance path; it is not a library retention policy. Native queries materialize AuditRecord
for consumer inspection, with no library query/authorization facade. Consumers validating
untrusted persisted data must check enum/key consistency before using it as application state.

## Deferred capabilities and required evidence

| Capability | Separate proof needed |
| --- | --- |
| Denial audit | Explicit failure/availability semantics and whether denial itself must be durable before returning. Accepted-change rollback does not settle this. |
| Required auditing of every operation | Concrete consumer policy and completeness enforcement. The library cannot infer audit selection from tracked entities. |
| Initiator propagation | Trusted producer mapping and schema evolution, preserving executing worker identity. Correlation/causation is not an actor. |
| Retention/query/export | Consumer visibility and retention policy, operational failure/restart and privileged database access. |
| Tamper evidence | Defined threat model, database roles, integrity format/key management and independent verification. Tracked immutability is narrower. |
| Other database providers | Actual provider JSON/time mapping, transaction/completion semantics, migrations and fault tests. |
| Cross-module atomicity | Named workflow with shared native connection/enlistment/commit/failure proofs. |

Historical archive proofs inform test intent; only current public-interface/consumer executions
in [the report](../../../docs/reports/e9-explicit-transactional-audit.md) establish new guarantees.
