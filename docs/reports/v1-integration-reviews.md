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
Follow-up inspection of this correction remains required.

## Remaining review gates

S1 consolidation/archive retirement needs its own two-axis review; package corrections need
verification and follow-up inspection.
The final complete diff then needs an independent Standards/Spec review and the requested
architecture/isolation review. This record does not close those gates or substitute historical
test counts for current execution.
