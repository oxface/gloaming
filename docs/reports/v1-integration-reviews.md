# V1 integration review record

Fixed integration base: `790a8b76b7a519039f61434f330d2ed1b07d70e9`.
The owner requested the two-axis code-review skill after each slice and again on the final
integration, followed by an independent architecture review. Reviewers inspect code and
evidence; their inspection does not constitute a fresh test run. Test execution belongs in
each slice report and the final verification record.

## WF2 deployment continuity

Reviewed `git diff 790a8b7...fa03a5e`; both references resolve and the diff is nonempty.

### Standards

One P3 finding: add a blank line after the completed `using` block before `break` in the
deployment example's `Application.cs`. No additional documented or heuristic finding.
The formatting correction is applied on integration. The integrated deployment suite then
passed all seven real-PostgreSQL tests (zero skipped), recorded in
`/tmp/v1-integrated-workflow-tests.log`.

### Spec

No findings. The earlier/current executables cover retained instances and replies,
incompatible definitions and wire inputs, compatible-code restoration, explicit signals,
and an interruption after save but before transaction completion. This is a bounded consumer
deployment procedure, not a new definition-migration engine.

## P1 projection assessment

Reviewed `git diff 790a8b7...4bed3eb`; both references resolve and the diff is nonempty.

### Standards

No findings.

### Spec

No findings. Native query evidence is distinguished from the separately selected B2
receiver-owned view. There is no claim that the assessment itself implements B2 or proves
its population protocol. No generic projection engine is justified by the inspected need.

## T2 Catalog patterns and optional worker

Reviewed `git diff 4bed3eb...1b5f4f7`; both references resolve and the diff is nonempty.

### Standards

One grouped P3 finding: ten missing statement separators in generated Catalog commands,
filters, worker entry and two test files. Corrected in `4a604df`; ignoring blank lines leaves
no behavior change in that correction.

### Spec

No findings. The generated commands retain native transaction/save ownership and version
checks; SQL filters precede deterministic pagination/counts. The optional read-only worker
establishes fresh trusted contexts and has executable process outcomes. Default generation
omits worker/messaging/event composition. No additional reusable runtime is demonstrated.

## B2 Dispatch population

Reviewed `git diff 790a8b7...136bcb6`; both references resolve and the diff is nonempty.

### Standards

Two documented findings: record accepted, expensive-to-reverse population/revision decisions
in an ADR (P2), and restore required statement separators (P3). Two heuristic findings:
duplicate Organization admission predicates and independently declared subscription constants
can drift. Small consumer-local reuse is justified; a general replication engine is not.
Corrections and their verification are recorded in the B2 report.

### Spec

One P2 finding: a schema-1 payload omitting `Listed` deserializes as `false`, potentially
withdrawing a listing and advancing its revision fence without an explicit withdrawal.
Require the wire discriminator and prove malformed input cannot change the view or complete
processing. A follow-up Spec inspection verified required identity, revision and `Listed`
members and both real relay/inbox regression paths; the P2 is resolved with no new finding.
The implementing agent ran all 14 PostgreSQL tests successfully, zero skipped. Otherwise the
inspected implementation fulfills its baseline-complete/eventual
overlap contract; it does not promise a coherent cross-key snapshot. Canonical host integration
remains a separate integration obligation. Corrections are recorded in the B2 report.

## Package readiness

Reviewed the package/metadata/documentation/tooling paths in
`git diff c346a51...1d5a7f9`; both references resolve and the scoped diff is nonempty.

### Standards

Two P2 findings: `CollectFrom<TEntity>` incorrectly describes assignable tracked entities
as an exact CLR type selection; correct the XML summary. Ordinary SDK packing also needs
the same usable README links as the proof artifacts: the first harness rendered links
through an override while default packing retained source-relative links. Fix the ordinary
packing path and verify its artifacts directly. No additional reusable runtime mechanism
is justified by small assertion helpers or explicit consumer composition.

### Spec

No findings against the bounded local-artifact/clean-adoption scope. The reviewer independently
inspected all 23 archive hashes, the manifest hash, package-only dependency assets and retained
execution logs for all four profiles; no tests were rerun. Native PostgreSQL assertions cover
the selected transaction, fault and fresh-context outcomes. Broader functional guarantees and
final integration review remain separate.

The public summary correction is applied. Native default packing now renders and validates
README links before NuGet collects files, without Node/npm. Fresh ordinary SDK packing passed
all 23 archives and four clean consumers, including PostgreSQL; native renderer encoding,
separator and failure probes, no-build packing and external README override also passed.
The package report records the new evidence separately from the superseded custom-input run.
Follow-up Standards and Spec inspections of `04ef50b...4eda8d0` both report zero findings.
Standards confirmed both P2 corrections; Spec independently checked the ordinary-pack manifest,
all archive hashes, package-only dependency assets and retained consumer/renderer logs. Neither
reviewer reran tests. The two findings are resolved.

## Canonical Directory adoption and route selection

Reviewed `f8b1969`, followed by the private policy correction `3b8dbde`.

### Standards

One heuristic P3 finding: restricted and ordinary broker roles repeated the same Sales
route/lease/retry policy. A private `SalesDispatchOptions()` factory removes that duplication
without hiding context credentials, role branches or publisher registration. Follow-up
inspection reports zero findings. The owning report records its focused build/style checks.

### Spec

No findings. The actual API profile edit, separate Worker relay and receiver search exercise
the public contract with source tables unavailable at search time. `RouteKeys` addresses a
demonstrated shared-outbox claim obligation; business disclosure/admission and native
transactions remain consumer-owned. Provider tests retain the existing lease protocol.

## S1 consolidation and archive retirement

Reviewed the complete S1 scope in `790a8b7...10d938f`, including proof transfers and retirement.

### Standards

One P2 finding: two relocated family examples executed only in the Wholesale job, but their
source paths did not select that job. One grouped P3 finding: two missing statement separators.
Working-tree corrections move the examples to Tenancy/Persistence, add actual dependent
inputs and both Wholesale infrastructure fixtures, and insert the separators. The
[repair report](s1-ci-selection-repair.md) records 1,921 covered job/input pairs and 97 probes.

### Spec

One P2 finding: the same missing CI selection leaves the relocation proof map incomplete.
The correction preserves the command inventory while assigning each example its owning lane.
Inspection independently confirmed all 69 inherited product test files and 302 Fact/Theory
methods, retained migration/literal obligations, actual product entry points, independent
family composition and the exact 803-file retirement. No other extraction was demonstrated.

## Final integration review and remaining gates

Fresh independent Standards/Spec reviewers inspect `790a8b7...10d938f` and the exact pending
six-file correction separately. The final Spec inspection reports zero actionable findings:
the implementation satisfies the selected scope, with native/runtime execution and independent
architecture review still required. It independently verified the final package manifest hash.
The independent Standards inspection also reports zero new findings. It confirmed that the
pending correction resolves the earlier CI/spacing findings and found no further actionable
Fowler smell. Both axes distinguish worthwhile route-claim mechanics from consumer policy;
neither justifies a generic replication, command or deployment framework. This record does
not substitute historical test counts or review inspection for fresh runtime execution.

## P2 architecture and isolation review

After both final code-review axes completed, the requested architecture skill independently
explored the same complete integration diff and pending corrections, with a separate walker.
It read the existing glossary and relevant decisions, inspected actual module interfaces and
test surfaces, and applied the deletion test rather than inventing another framework.

**No required architecture change or additional worthwhile Gloaming mechanism was found.**
Route selection earns depth behind the existing claim/lease interface and has materially
different actual publishers. Directory merge/readiness remains private consumer policy.
Catalog commands/queries and native transaction ownership remain ordinary application code.
Independent family examples, module-owned contexts/migrations, explicit operation scopes and
the generated default's omission of messaging/events retain their documented isolation.

The standalone visual report is
`/home/oxface/.cache/tmp/architecture-review-20261022T192448Z.html`. It contains before/after
views, real ownership/test interfaces, supported assumptions and rejected speculative
abstractions. `xdg-open` was unavailable; the artifact can be opened directly. This temporary
visual supplements the durable decisions here and is not required by any runtime or proof.

Two speculative navigation changes were explicitly rejected for this increment, rather than
left as required v1 gaps: the optional Catalog worker's executable-project reference shares
editable application composition without executing the console entry point; removing it now
would add an assembly or duplicate/conditional composition for little leverage. The neutral
ChildHost helper has three explicit linked adopters with CI inputs; moving it to another
folder changes ownership/navigation but removes no demonstrated technical burden. Neither
candidate justifies a Gloaming registration/hosting runtime.

No repository implementation or tests changed during P2. Reviewer inspection is not fresh
runtime execution. Subsequent final native/runtime verification and the requirement audit
passed; the [execution record](v1-final-verification.md) retains the commands and evidence.

The [dated reference inventory](remaining-capability-reference-review.md) was also reconciled
with current mechanisms. Its external-source findings remain dated research, not newly checked
framework-version claims or a parity target:

| Reference pressure | Current decision and evidence surface |
| --- | --- |
| Stream writes and inline decision state | Keep typed stores, expected versions and required inline state in native module transactions; EventSourcing provider/adoption proofs own the contract. No snapshot/daemon dependency follows. |
| Durable handlers and recoverable publication | Existing inbox transactional ownership, outbox fenced leases, explicit scopes and optional native workers/session helper concentrate demonstrated correctness work. Actual broker outage, process-death and shutdown proofs remain active after consolidation. |
| Durable process continuity | Small typed continuation selection and atomic outgoing requests earn reuse; branching, compensation and compatible deployment remain consumer policy with separate old/new executable proofs. No scheduler/replay/multiversion engine is justified. |
| Replication and projections | Native EF handles selected local filters/joins. The independently available Directory needs disclosed revisions, overlap and consumer merge/readiness. Route claim selection is the demonstrated cross-publisher mechanism; there is no generic feed/projector requirement. |
| Distributed agent ownership and registration machinery | Competing row claims and leases already satisfy selected work. No unmet singleton scheduler/partition-owner requirement justifies leader election, code generation, another transaction runtime or a global registration abstraction. |

## Final runtime correction and follow-up review

Fresh runtime execution after the independent reviews found one stale S1 integration
expectation: the restricted browser setup still counted six protected Sales tables after B2
intentionally added protected outbox publication. The first run passed five of six cases.
The correction queries actual sorted role/table arrays and independently asserts the two
restricted roles and all seven forced-RLS tables, including `outbox_messages`. It changes no
production policy, provisioning, grants, startup guard or browser business/security assertion.
The complete real runtime rerun subsequently passed all six cases, zero skipped, in
`/tmp/v1-final-runtime-corrected.log`. The changed test also passed its build, CSharpier and
native style/analyzer checks. These are new execution results, separate from review inspection.

### Standards

One report-description inconsistency was identified: the introduction called the remaining
working-tree changes documentation-only despite the new test correction. The description is
corrected to name the test and pending verification. Follow-up inspection confirms zero
remaining findings, no new consumer burden and no production architecture change.

### Spec

The same report-description inconsistency was identified and corrected. The reviewer confirmed
that the independent exact-name check preserves S1's RLS/browser proof, matches the actual
seven-table policy and retains OIDC, admission, antiforgery, isolation, conflict and audit
assertions. Zero remaining actionable findings. The reviewer did not rerun tests or infer a
pass from the then-running process; the later complete execution is recorded above. No new
architecture or extraction opportunity follows from this test-only correction; the final P2
decisions remain applicable.
