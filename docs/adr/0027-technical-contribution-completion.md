# ADR 0027: technical contribution completion

Status: accepted architecture decision. Public interfaces/implementation receive owner review;
the complete exact change set requires explicit approval before commit.

## Context

Independent replies need retained named content, duplicate comparison and one terminal technical
outcome across concurrent arrivals and timeout. Consumers own participant/quorum choices,
authorization, business approval and relevance. The owner selected these technical mechanics;
the single-pending-request continuation runner remains independently usable.

## Decision

Use the existing optional PostgreSQL messaging package. An immutable complete work generation
retains explicit expected contributor identities, a consumer-selected required distinct count and
original completion/optional timeout envelopes. Native row locks serialize cooperating arrivals
and timeout. The selected count stages one completion notice; an eligible explicit timeout may
win first. A technical count is not a weighted/permission/approval policy engine.

Caller-owned native ReadCommitted save/commit retains head progress, immutable content and one
terminal outbox notice atomically. Optional timeout composes finite intents, with pending signal
suppression and receiver relevance checks for already released work. No worker, clock or generic
workflow graph is added. Private original/current/stored proposal evidence catches suppressed,
detached, prematurely accepted and savepoint-reverted participants.

Store roster and original terminal envelopes as versioned JSONB so native representation is
validated at registration. Contributions retain opaque name/version/JSONB. Require unique object
property names recursively before accepting producer payloads/templates; JSONB duplicate-key
collapse must not make an exact retry conflict or invalidate genuine stored evidence.

Consumers keep current-generation source locks, admission, threshold meaning, content readers,
business acceptance, schema/grants, rollout and evidence retention. Retain original keys/content
for module lifetime; explicit removal ends protection. Later format changes need retained fixtures
and actual old/new deployed readers/writers.

## Consequences

One module concentrates repeated contribution/terminal/timeout atomicity without another package
or dependency. Import and offer consumers demonstrate different counts/readers/business outcomes.
Arrival reads no clock: the native lock winner determines terminal closure after timeout eligibility.
Strict business deadlines, FIFO, fairness and exactly-once external effects are not supplied.
The [owning contract](../../src/Gloaming.Messaging/docs/contributions.md) defines supported setup
and limits; process/provider/package proofs establish different parts of that contract.
