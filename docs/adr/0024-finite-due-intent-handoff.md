# 0024: Retained finite occurrences and atomic outbox handoff

Date: 2026-10-24. Status: accepted design; implementation receives the repository's normal
owner review. The [owning contract](../../src/Gloaming.Messaging/docs/due-intents.md) defines
the complete public setup, supported model and limits.

Personnel reminders and Growth preparation repeat occurrence storage, pre-handoff suppression
and bounded atomic outbox release while retaining different generation/relevance policy.
Keep those mechanics in the existing optional PostgreSQL messaging package, with no new
clock, host, serialization, tenancy or command-receipt dependency. A separate journal keeps
cancellation and retained occurrence identity distinct from retry eligibility and delivery ID.

Stage/Suppress/Release use caller-owned native ReadCommitted transactions and explicit UTC
microsecond instants. Release holds pending occurrence locks through commit, stages the original
envelope in the same context's outbox, and changes lifecycle atomically. The save guard verifies
both participants, including native stored evidence for unchanged proposals. No expiring claim
or external publication happens while holding these locks. This favors bounded, checkable
handoff over a supplied scheduler runtime or hidden business execution.

Scope is an opaque technical selection independent of optional envelope tenancy. The fixed
complete key, unconditional delivery-ID uniqueness, lifecycle numbers and provided storage
mapping are compatibility obligations. Retain terminal evidence for the module lifetime;
there is no automatic TTL or identifier reuse. Consumer migration authority owns deployment
and grants; a restricted module worker is distinct from tenant RLS or business permission.

Suppression cannot recall released work. Receivers still establish admission and check current
source/generation/relevance under their own concurrency protocol. Consumers own calendars,
recurrence anchors, recipient decisions, polling and final save/commit. Recurrence and workflow
deadlines remain separately selected mechanisms rather than meaning inferred from an occurrence.
