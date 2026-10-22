# T1: bounded template population rehearsal

Status: completed, owner-approved and checkpointed as `8ccf4c8` on 2026-10-06, including
the three review corrections. [The report](../reports/t1-template-rehearsal.md) records
the implemented composition and execution evidence; [the creator guide](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/README.md)
is the current invocation reference. This brings a small portion of E10 forward, not the
whole CLI or an option matrix. The original implementation brief is retained below.
The next proposed task is [ES1 bounded event append](es1-bounded-event-append.md).
Read [the review](../reports/strategy-review.md),
[repository workflow](../conventions/repository.md), [design](../design.md), and
[the current plan entry point](library-extraction.md) first.

## Outcome and scope

Produce one ordinary consumer-owned .NET repository from explicit configuration and prove
that it works outside the Foundry checkout. It adopts already-reviewed foundations and
contains no required event or messaging projects, packages, registrations, migrations or
workers. A configurable application name and root namespace are enough for the first
population proof. No transport/provider matrix is required.

Before coding, present the exact generated tree and executable consumer journey. Prefer a
small state-stored, tenant-aware read using the existing native module/Contracts and EF
ownership recipes. Authentication, tenant admission and database initialization must be
explicit; no request-supplied actor identity can become a production trust mechanism.
Wholesale business behavior is reference sample material, not compulsory generated policy.
Select which small editable example earns a place in the output rather than copying the
whole sample or adding empty future capability projects.

The exact minimal host/example composition remains for owner review. Avoid requiring
OIDC provisioning, Aspire, Sales mutation or the full Access lifecycle merely to exercise
population; use established compositions where chosen and document any limited runtime
recipe honestly. Creation of a richer preset can follow independently.

## Implementation decisions to resolve in that session

Evaluate native `dotnet new` authoring first. It already supports multi-project input,
parameters and conditional files. A custom CLI may supply configuration-file translation
or repository-specific checks if that removes a demonstrated obligation. Do not build a
general replacement language or runtime configuration framework.

Choose one library adoption mechanism that genuinely works outside this repository.
Project references escaping back into Foundry are not an acceptable generated-consumer
proof. For an initial unpublished baseline, materializing the selected library source is
a possible bounded approach; locally packaged libraries are another. Decide and document
this before implementation. Publishing packages is not part of T1.

Initial creation should target a new output directory and refuse conflicting/nonempty
destinations before changing files. Repeating creation against existing output must refuse
without mutation unless a separately reviewed idempotent contract is selected. Updating an
already customized application is a later capability, not a hidden requirement.

Keep the generated native registration, provider setup, saves and migrations editable.
Do not introduce a runtime dependency on the population tool. Review any new interface
with concrete usage, errors, ownership, dependencies and relevant proof obligations before
implementing it, as required by the existing extraction gate.

## Executable proofs

- Generate two differently named consumers into temporary directories outside the checkout;
  restore/build both and exercise their selected capability through the generated interface.
- Prove tenant isolation and missing-context behavior at the selected consumer seam using
  the existing library guarantees, without reimplementing all foundation test matrices.
- If the selected output includes EF migrations, apply them to a fresh disposable database
  and check the intended module ownership. Prove startup does not migrate or seed implicitly.
- Inspect the generated dependency graph and source for omitted event/messaging setup and
  references to Foundry checkout paths, archive material or Wholesale-specific names.
- Compare the generated file list/content for identical inputs, excluding explicitly
  documented nondeterministic tooling output. Check naming in solution/project/config paths.
- Invalid configuration, occupied output and repeated creation must fail without partially
  changing the destination. Reject unsupported combinations explicitly.

CI should execute this supported creation proof. Tests protect the generated adopter
experience and failure behavior; avoid a large matrix of unimplemented choices.

## Session constraints and stop condition

The owner instructed **no deletion yet** for existing committed and uncommitted event work.
T1 does not authorize removal, broad module splitting or repair work. Check the worktree at
session start and keep those existing edits distinguishable from the new capability.

Stop after one generated composition, its external consumer proof and documented supported
configuration are reviewable. Leave all changes unstaged. Report library/template/sample
findings separately. No new runtime mechanism is expected; creation/omission behavior may
be the newly proven template mechanism. Every commit still needs approval of the exact
complete change set.

If the owner prioritizes event sourcing instead, begin with a separate contract-design
session: supported storage/load/append behavior, one state-dependent business decision,
caller transaction ownership, deliberate exclusions, and a genuinely different second
adopter. Do not infer a projector interface or start E6.2 from the archived backlog.
