# Repository workflow

## Reviewable changes

Deliver one coherent outcome with implementation, consumer usage, relevant tests and
documentation. Keep the repository valid and the change understandable in one review.
Public library interfaces and implementations receive owner review line by line.
Use [library design conventions](library-design.md) when changing a capability boundary.

Preserve a user's staged review snapshot. Agent edits stay in the working tree until staging
is explicitly authorized. Work on topic branches and integrate through pull requests;
publish a topic branch to its own remote branch.

## Validation and compatibility

Run checks appropriate to the change and complete required repository checks. Explain what
was actually executed and its limits. Tests for native providers, package-only adoption and
process recovery protect different boundaries; test presence or a matching count proves
no execution.

Preserve owned payloads, schemas and migrations where a supported compatibility guarantee
depends on them. Moving or removing projects must account for linked inputs, CI selection,
documentation and unique proof obligations. Assess required gaps before declaring a
capability complete; successful substeps do not close its remaining adoption or deployment work.

## Repository scripts

Use TypeScript for new durable repository scripts and proof harnesses. Document their runtime
and invocation, and verify types and formatting. Preserve unrelated existing scripts.

## Documentation

Public documentation covers current setup, accepted decisions, architecture, conventions,
guarantees and limitations. Library contracts live beside their source. The
[application patterns](../../modular-monolith-patterns/README.md) describe consumer structure
and implementation tasks. Link to current source and proof owners when examples help.

Store research, comparisons, proposals, plans, execution logs and local work queues in ignored
docs/internal/. Public guides and package READMEs must work in a clean checkout without that
directory. Keep essential supported limitations in the public contract even when their
discussion is internal.

## Handoff and commits

Report the outcome, relevant verification, review-worthy files, limitations and remaining gaps.
Distinguish reusable mechanisms from consumer policy, and fresh results from historical evidence.

The repository owner must explicitly approve the exact complete change set before every
commit. Task authorization, successful checks, silence and earlier commit approval do not
authorize a later commit. If the proposed files change after approval, obtain approval for
the updated set. Include staged and unstaged work in the handoff.
