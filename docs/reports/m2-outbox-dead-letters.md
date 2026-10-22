# M2 outbox dead letters and operator retry

Date: 2026-10-21. Interface/scope approved by the owner's “proceed with implementation”.
Implementation is prepared for owner line-by-line review on `feat/outbox-dead-letters`,
based on fetched main `f4f694018b03c0d43c1707086ca3673e0352a6d6`. No commit or push was made.
The [reviewed proposal](../plans/m2-outbox-dead-letters.md) records the opening baseline;
the [family contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md) defines current
consumer setup, guarantees, deployment obligations and limits.

## Outcome and ownership

New reusable mechanisms are proven: lease-fenced publisher-exception accounting with
consumer-selected retry/dead-letter state, bounded safe metadata inspection, and
revision-conditional operator retry of the unchanged retained outbox message. Attempts
still counts committed claims. Separate failure counts describe committed observations
of publisher exceptions; they do not certify transport rejection or count every execution.

Publication stays outside database transactions. Completion and accounting lock the row,
then recheck the current token and database-clock expiry at their final writes. Completion
errors never invoke publication policy or a compensating failure write. An obsolete
publisher can still accept externally after successor dead-lettering/operator retry; its
database updates remain fenced. This preserves the receiver's need for deduplication.

One IOutboxDeadLetters combines List/Get/Retry; no second recovery interface is introduced.
The inbox keeps its public declarations and physical schema. A private timestamp/UUID
position codec is shared, with independently proven compatibility for its existing token
format. Route/subscription identity, metadata types, selection and transaction protocols
remain separate. A common queue record/store, provider dialect and generic administration
framework were rejected because the local rollback and external publication semantics differ.

Consumer code still owns classification, budgets/delays, acceptance conditions, safe reason
vocabulary, tenant admission, operator authority, presentation and audit. The library owns
the conditional transitions and accounting-fault signal; native .NET hosting owns supervision.
There is no new library package/dependency or template preset.

## Executable adoption and schema findings

[OutboxDemo](../../src/Gloaming.Messaging/samples/OutboxDemo/README.md) keeps its finite EF submit/dispatch journey
and adds explicit --setup, --serve and --serve --worker modes. Its HTTP policy stops
unsupported contracts immediately and bounds selected transient observations to three per
cycle. Optional authenticated operator endpoints fix exports.render and return metadata
only; retry accepts an observed revision and changes eligibility. An operator-only host
needs neither publisher nor receiver URI. ASP.NET Core is an explicit sample dependency;
the architecture check permits it only for this adopter, leaving library restrictions intact.

Forward Exports/Inventory/Sales migrations add eight lifecycle columns, consistency constraints,
an adjusted candidate index and a partial dead-letter lookup index. Pending/leased/completed
legacy work retains every preexisting column, including Attempts, envelope and completion.
New failures/revision/redrive metadata starts at zero/null; history is not inferred from claims.
Snapshot changes are limited to the outbox mapping, preserving unrelated generated model order.
Historical migrations and the frozen archive are unchanged.

Restricted Sales dispatch gains only six accounting-column UPDATE privileges beyond its
existing five delivery fields. It retains no operator-only redrive UPDATE, payload mutation,
producer INSERT or business access. Actual PostgreSQL credentials exercise both tenants
under the existing command-specific RLS policies. Startup validates missing/excessive grants.

Compatibility verification also exposed an existing restricted **producer** obligation:
EF's INSERT RETURNING now reads revision, failure_count, failures_since_redrive and
redrive_count defaults. SalesReplyDatabase therefore provisions/verifies SELECT on these
four columns alongside queued_at/available_at. It still grants no payload SELECT or outbox
lifecycle UPDATE. Existing reply and dispatch roles both need reviewed grant updates before
upgrading. Narrow operator inspection also needs SELECT on lifecycle predicates, including
dispatched_at, beyond the projected metadata. A separate proof credential can inspect/retry
without payload SELECT.

The library's optional worker faults on OutboxFailureAccountingException after a distinct
critical diagnostic and makes no further poll. StopHost/Ignore behavior is verified natively;
the sample chooses StopHost. Repair/restart is consumer policy, and retry does not restart
a faulted worker. Direct callers retain message identity and both original errors. The new
optional constructor parameter preserves three/four-argument source calls; consumers rebuild
against the current package/model rather than mixing old binaries with dead-letter state.

## New proof evidence

All persistence claims below use real disposable PostgreSQL 18.6. HTTP proofs use actual
Kestrel requests, and process proofs launch the built OutboxDemo executable. Native
pg_blocking_pids/advisory-wait and database-clock observations establish race ordering.
Post-commit response-loss injection throws only after an actual PostgreSQL commit.

| Guarantee | Executed evidence |
| --- | --- |
| Durable budgets and repeated retry | Fresh-context cycles retain lifetime failures/Attempts and reset only cycle failures. Repeated stale retry cannot reopen later dead letters; current publisher validation can stop the unchanged schema again. Completion retains evidence and rejects retry. |
| Competing dispatchers and stale publishers | A dispatcher skips a row whose accounting is held at a native advisory barrier and completes unrelated work. Old publisher success/failure after successor dead-lettering and operator retry cannot change its revision, eligibility, counts or lease; external acceptance remains possible. |
| Expiry while finishing waits | Accounting and completion wait on an independently held row lock; database time confirms expiry before release. Neither can then count, stop or complete the message, even while the token is unchanged. Successor dispatch remains possible. |
| Competing operators | Two qualifying requests are observed waiting on a row lock. They recheck after release and apply once; inspection remains live and the old revision cannot reopen the subsequent dead letter. |
| Completion ambiguity | A real HTTP receiver accepts before completion SQL fails. Publication policy is not invoked and FailureCount stays zero; lease expiry redelivers the identical ID/payload. A separate post-real-commit fault preserves completed state without compensating accounting. |
| Publisher ambiguity | HTTP receiver consumes a request and loses its response. The valid-claim exception is counted as an observation, with no assertion that the receiver rejected it. |
| Unconfirmed accounting | Throwing/null/invalid policy, SQL constraints, missing grants, actual backend termination and post-commit response loss preserve publication/accounting errors. No partial metadata or unconditional lease release occurs. |
| Supervision and cancellation | A worker makes one publication then faults under policy/SQL accounting faults. Native StopHost/Ignore applies. Cancellation during accounting preserves direct-caller errors; worker shutdown during a verified lock wait ends without another publication. |
| Real process death/restart | Kill during the second accounting transaction leaves the first failure committed and the second claim uncounted as failure. Restart reaches the persisted threshold; another restart processes unrelated work while the original remains stopped, then authenticated retry completes the same message. |
| Accounting-fault repair/restart | Persistent accounting SQL fault stops the built host after one request/critical diagnostic. Repair and restart completes the same retained message after lease expiry without inventing failures. |
| Inspection and authority | PostgreSQL UUID/time paging, exact route/code selection, cursor bounds/scope rejection, disappearing live rows, metadata exclusion, fresh-context guards, SELECT-only inspection and separately granted operator retry without payload access. HTTP credentials reject before unavailable storage; configured endpoints fix the route. |
| Schema and composition | Legacy pending/leased/completed rows in all three schemas preserve original state; snapshots match current models. Producer guards reject assigned recovery state. Existing inbox, continuation, restricted-role and consumer composition checks are included below. |

## Verification

Fresh runs use .NET SDK 10.0.401 and the repository's local rootless Podman socket:

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
dotnet test --project PROJECT --no-build --no-restore --no-progress --no-ansi \
  --output Normal --show-stdout Failed --show-stderr Failed
```

| Suite | Fresh result |
| --- | --- |
| OutboxPostgresTests | 74 passed, 0 failed, 0 skipped. |
| InboxPostgresTests | 67 passed, 0 failed, 0 skipped. |
| OutboxDemo.Tests | 12 passed, 0 failed, 0 skipped. |
| MessagingWorkerDemo.Tests | 12 passed, 0 failed, 0 skipped. |
| WorkflowPostgresTests | 38 passed, 0 failed, 0 skipped. |
| MessagingTests | 8 passed, 0 failed, 0 skipped. |
| WorkflowTests | 8 passed, 0 failed, 0 skipped. |
| ArchitectureTests | 96 passed, 0 failed, 0 skipped. |
| WorkflowDemo.Tests | 148 passed, 0 failed, 0 skipped, including restricted Sales dispatch/reply roles and both module upgrades. |
| InboxDemo.Tests | 23 passed, 0 failed, 0 skipped. |
| LocalWorkflowDemo.Tests | 14 passed, 0 failed, 0 skipped. |
| EventPersistenceDemo.Tests, DispatchTests filter | 18 passed, 0 failed, 0 skipped. |
| EventReactionsDemo/Consumer.Tests | 104 passed, 0 failed, 0 skipped; existing save-capability composition. |

Total: **622 passed, 0 failed, 0 skipped across 13 suites**. The full active solution builds
with zero warnings/errors. Native style/analyzer verification and CSharpier including generated
files pass. Archive verification preserves all 800 original files. Document-link/whitespace
checks and git diff --check pass; git diff --cached remains empty with its original SHA-256
`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.

Local command logs were retained under `/tmp/m2-outbox-*.log` for this review session. These
are disposable execution logs, not a new repository proof harness or maintained artifact.

Opening main baseline reruns (36 outbox, 66 inbox, six HTTP adopter tests) are historical
comparison for this implementation; they do not prove M2 outbox behavior. Earlier O1/I1,
A2.8 and inbox M2 reports remain separately dated evidence. Initial verification failures
revealed the restricted producer default-read obligation, narrow operator predicate grant,
sample dependency declaration and legacy-fixture ownership requirement; those were corrected
before the successful final runs. The incomplete workflow run using the old producer grants
was cancelled rather than counted as a successful result.

## Review order and remaining limits

Review [public contracts](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IOutboxDeadLetters.cs),
failure inputs/decisions and [the callable protocol](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs)
first. Then inspect [metadata/retry SQL](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDeadLetters.cs),
model/producer guards, worker supervision, the three forward migrations and
[restricted grant inventories](../../samples/Wholesale/Worker/Broker/SalesDispatchDatabase.cs),
including SalesReplyDatabase's generated-default reads. Finally review executable policy/
operator endpoints and the independent PostgreSQL/process assertions. The
[accepted decision](../adr/0019-lease-fenced-outbox-dead-letters.md) records the durable boundary.

Accounting budgets do not cap crashes, stale claims, uncertain completion or external
acceptances. Dead letters do not cancel old publishers. The stopped worker supplies no
progress until repair/restart. Deployment must apply migrations and reviewed grants and
stop binaries that ignore dead-letter state; online mixed-version rollout is unsupported.
Database column privileges cannot enforce legal lifecycle transitions against a compromised
credential. Classification, operator admission, audit and operational supervision remain
consumer responsibilities.

Payload editing, completed-message replay, retention and lease renewal remain excluded.
No global administration UI, durable operator audit store, broker framework, additional
provider certification, throughput benchmark or template preset was added. Both implementation
and documentation remain unstaged, and the originally empty index is preserved for owner review.
