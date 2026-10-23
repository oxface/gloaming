# ADR 0007: Pre-read admission for version-preserving inline rebuilding

Status: superseded by [ADR 0008](0008-native-optimistic-aggregate-rebuilding.md). Historical owner-reviewed direction, 2026-10-07; implementation remains available for line-by-line
review. [The reviewed ES2 scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-single-stream-rebuilding.md) adds one maintenance
capability to the explicit native EF transaction contract.

Repair rebuilds the one required inline aggregate state at the existing event head without modifying events,
stream versions or recorded times. Optimistic append predicates alone cannot exclude a writer
that already loaded corrupt state at that same version. A repair-only header row lock was
rejected by [a native PostgreSQL diagnostic](../reports/es2-rebuild-design.md): both operations
committed and the writer overwrote the repaired state.

Repair-enabled writers take shared admission **before** loading, and rebuilding takes exclusive
admission before capturing the header. Scope admission to the complete physical stream key,
including ownership and schema/table, rather than the archive's Organization-wide gate.
Require ReadCommitted and a fresh context for one terminal rebuild; ordinary shared writers
retain multiple-key composition and native optimistic competition. This does not establish
business-key discovery, multi-stream repair or safety for external writers bypassing admission.

The owner-review refinement makes this protocol part of main inline-state registration for
every registered aggregate. Every such store supplies replay and a compatible gate before
writing; there is no separate rebuild opt-in. Raw history-only streams remain independent.
Maintenance invocation is explicit: ordinary fetching still rejects broken state. This retains
safe same-version repair while removing a redundant configuration choice; it does not add
automatic repair, a scheduler or a general projection engine.

Keep the gate lifecycle/model/save association in EventSourcing.EntityFrameworkCore and the
parameterized PostgreSQL advisory transaction-lock implementation in an optional Postgres
package. Consumers retain codecs, evolution, authorization, typed contexts and save/commit.
A private exact prepared aggregate replacement permits state-only saves without a public bypass.
Changing key encoding or deploying ungated writers alongside repairing binaries needs separate
coordination. Raw streams and the event-free T1 template remain independent adopters.
