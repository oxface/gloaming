# Repository workflow

## Reviewable increments

Deliver one coherent outcome per increment, including its implementation, consumer usage,
tests, and documentation. Keep the repository valid and the outcome understandable in one
owner review. Split independent outcomes rather than introducing empty layers or broken
intermediate states.

Library interfaces and implementations receive line-by-line review. Sample usage and proof
changes accompany the library so the consumer obligations can be assessed together. A new
template file is needed only when the increment establishes a reusable setup pattern.

## Repository scripts

Use TypeScript for new durable repository scripts and proof harnesses, and when actively
reworking such tooling. Document the runtime and invocation, and verify types and formatting.
Preserve unrelated existing scripts; this convention does not authorize a repository-wide
migration. Retired tooling remains historical Git evidence through the
[archive retirement record](../reports/s1-archive-retirement.md).

## Handoff

Present the coherent outcome, important choices, review-worthy files, verification, risks,
and remaining gaps. Each slice report also distinguishes library mechanisms from template
setup and sample policy, and states which guarantees were tested, rejected, proposed, or
inherited only as historical evidence. State explicitly when no new reusable mechanism
was proven.

## V1 scope and remaining gaps

Assess known gaps before declaring a slice or capability block complete. Important gaps
needed for the selected v1 outcome remain open in that block and must be resolved there,
even if implementation spans several reviewable PRs. Merged sub-slices are not evidence that
the whole block is complete. Do not close a block and then present its known remaining work
as an unrelated new slice.

Each report records which gaps are resolved, which remain required before closure, and which
have an explicit owner-approved deferral until after v1. Record the supported behavior and
limitation beside the owning library. Do not automatically defer important gaps or silently
add unrelated enhancements to v1. A defect that breaks a supported guarantee must be fixed
before claiming completion, or the contract narrowed with owner review. Interface and commit
review requirements still apply.

The [roadmap](../plans/remaining-capability-roadmap.md#v1-scope-and-gap-disposition)
records current block status and explicit dispositions. Reconcile older gaps against later
implementation/evidence before reopening work. Distinguish functional defects, missing
operational proofs, consumer responsibilities and deliberately unsupported capabilities.
A closed sub-slice does not close its owning block's deployment or adoption obligations.

Before v1 sign-off, execute the [sample/experiment consolidation](../plans/s1-sample-experiment-consolidation.md)
and separate independent code and architecture reviews. Relocation/removal must preserve
unique proof obligations and update CI, documentation and generated-template references.

## Commit approval

The repository owner must explicitly approve the exact complete change set before every
commit. Task authorization, plan approval, successful checks, silence, and earlier commit
approval do not authorize `git commit`.

Present the implementation and documentation changes, verification, and known limitations
before requesting commit approval. If the proposed files change after approval, obtain
approval for the updated change set. Include staged and unstaged work in the handoff.

## Library documentation

Keep each library's consumer setup, current capabilities, limitations and deferred-feature
context beside its source, in its README and local docs directory where useful. A consumer
should not need the repository's root plans to discover an essential guarantee or integration
obligation. Keep related package composition and optional dependencies explicit.

Root design decisions, ADRs, interface proposals and slice reports retain cross-cutting review
and dated evidence; link them to the owning library's current capability record. Mark future
features as deferred rather than supported, including their known proof obligations. Moving
project/test folders is a separately reviewed relocation with references, template snapshots
and checks accounted for; documentation locality does not require moving everything at once.
