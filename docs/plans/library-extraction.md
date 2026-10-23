# Library extraction gate

Use the [library audit](../reports/gloaming-library-audit.md#remaining-cleanup-slices) for the
single current work queue. This document governs new mechanism proposals; it is not another
implementation sequence. Current contracts and deferred-feature context live beside each
library. [Design](../design.md) records cross-family ownership, and
[repository workflow](../conventions/repository.md) governs review and checkpoint approval.

## Before proposing an interface

1. Identify a concrete repeated technical obligation in executable consumers. Describe the
   failure or integration complexity a library would remove, including ordinary native APIs
   that may already suffice. Keep business rules and module Contracts with their consumers.
2. Show the complete adoption surface: dependencies, registration, ordering, context/transaction
   ownership, supported storage model, cancellation and failure/cleanup behavior. Explicit
   selection alone does not establish useful abstraction value.
3. Compare a bounded mechanism with native consumer composition. Provider-specific protocols
   may be appropriate; provider neutrality requires actual alternative-provider evidence.
4. Propose one coherent change with executable public-interface usage, independent expected
   outcomes and relevant failure proofs. Include documentation, fixtures/migrations, CI and
   pruning in the same reviewable outcome. Public library code receives owner line-by-line review.
5. Record the decision and exact supported limits with the owning family. Use an ADR for a
   settled hard-to-reverse choice. A rejected proposal earns no placeholder package or runtime.

## Current proposal status

No additional public interface is selected by the cleanup. The hosting separation, continuation
package consolidation and thin-adapter retirements are implemented; their checkpoint/evidence
records are in the audit. Optional execution may own a bounded transaction or invoke a consumer
handler under an explicit contract. Consumers retain all authority outside that delegation.

Event-sourcing projections, permanent inbox duplicate registries, automatic maintenance,
human-workflow assignments and broader providers/transports remain governed by their owning
families' documented unsupported/deferred scope. They are not implicit follow-up tasks.
A concrete Umbrine requirement can justify a future proposal without expanding this cleanup.

## Historical proposals

The E0–E10 sequence, capability roadmaps, integration ledger and dated interface plans were
retired after the library-first audit. Their Git provenance, preserved proof ownership and
reference policy are recorded in the [retirement record](../reports/s1-archive-retirement.md).
Historical approval text applies only to its original task; current work follows the workflow
and the owner's current instructions. Retiring a plan neither removes a supported guarantee
nor certifies an old proposal as implemented.
