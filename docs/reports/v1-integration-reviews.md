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

## Remaining review gates

S1 consolidation/archive retirement and package readiness need their own two-axis review.
The final complete diff then needs an independent Standards/Spec review and the requested
architecture/isolation review. This record does not close those gates or substitute historical
test counts for current execution.
