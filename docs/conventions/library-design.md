# Library design conventions

Gloaming removes repeated technical complexity while consumers retain application authority.
Use these criteria when introducing or changing a library capability.

## Establish the obligation

Identify a concrete repeated failure or integration protocol in executable consumers.
Compare it with ordinary native APIs, including configuration, ordering, cleanup and
operational cost. A useful native application recipe can be the right outcome.
Keep domain rules, authorization policy and module Contracts in their owning application.

## Describe the complete adoption surface

State the selected dependencies, registration, native model, transaction/context ownership,
ordering, cancellation, failure disposal and deployment obligations. Show consumer code
using the public API. A fixed technical table or provider-specific mechanism is acceptable
when its supported model and constraints are explicit.

A deliberately invoked operation may own a bounded transaction or call consumer code.
Document exactly what is delegated. Other composition, schema/migration, transport and
operational decisions remain consumer-owned.

## Verify the contract

Provide independent expected outcomes and relevant failure proofs: native rollback and
concurrency, uncertain completion, process recovery and retained-data compatibility where
the capability claims them. Include an independent adopter and package-only consumption
when establishing a new package boundary. Actual provider tests establish provider support;
an abstraction alone does not.

Account for credentials, schema rollout, old/new writers and readers, stored identities,
retention and removal. Required gaps remain open until resolved or explicitly deferred.
Document supported limitations beside the owning package.

## Review and maintain

Deliver implementation, consumer usage, tests, documentation and CI selection together.
Public interfaces and implementations receive owner review line by line. Record accepted
architecture decisions in ADRs; proposals do not become supported features through prose.
Keep essential consumer obligations in the local library contract rather than a work report.
