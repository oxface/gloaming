# B1: closed replayable-source assessment

Status: closed by owner direction on 2026-10-14. Retain findings and dated verification
only. The owner rejected keeping the experimental implementation; source, tests, migration,
snapshot, reconstruction patch and preservation-specific configuration have been removed.

## Outcome

The [retention and public-contract assessment](../reports/b1-retention-and-contract-assessment.md)
is the accepted slice result. It distinguishes historical replay from current-state transfer,
records the existing outbox and version semantics, and compares storage alternatives without
selecting a general solution. The [dated experiment report](../reports/b1-replayable-integration-feed.md)
records what the discarded protocol proved and why that did not establish adoption value.
[ADR 0015](../adr/0015-producer-owned-retained-profile-feed.md) records its rejection.

The original experiment started from merged WF2 at
`d674e39a95f4aec8fa617b3382d423dc748bf166`. It used selected, retained Sales profile
after-images, an Organization writer gate, a committed head and retention floor, bounded
paging and a receiver receipt/checkpoint journal. Its PostgreSQL/process executions remain
documented evidence for that specific protocol. They are not a supported library capability
or a maintained executable example.

## Constraints carried forward

- Historical replay needs retained public messages or sufficient retained facts with an
  explicitly defined derivation. A current-state export or lookup has a different contract.
- Private domain events and event-sourcing facts are not automatically a public integration API.
  DDD does not imply persistent domain events.
- Existing outbox rows retain selected payloads, but delivery lifecycle and timestamps do
  not provide a safe ongoing replay cursor or complete state bootstrap.
- Public-state revision merging is conditional on a producer-defined key/revision and
  compatible updates. Aggregate versions do not order arbitrary multi-aggregate messages.
- A tested feed mechanism does not justify coupling normal Sales writes to it without
  a concrete receiver requirement.

## Later work

Revisit these findings only when a later slice identifies one producer/receiver operation,
the required facts, freshness/availability behavior and retained-history requirement.
Select the smallest suitable transfer protocol then. B2 snapshot/bootstrap implementation
remains outside this assessment; no custom feed, generic revision interface, streaming runtime
or library extraction is selected. Common Wholesale remains free of the discarded experiment.
No new reusable mechanism was proven.
