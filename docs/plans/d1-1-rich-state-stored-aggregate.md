# D1.1: rich state-stored aggregate and relational children

Date: 2026-10-14. The owner agreed this follow-up scope while reviewing D1.
This is a later bounded increment, not an expansion of the current D1 implementation.
No new library interface or aggregate base is selected.

## Purpose and consumer boundary

D1 demonstrates immutable Sales profile decisions over a customer and one address, compared
with event-sourced Purchasing decisions whose lines are stored within JSON state. It does not
exercise a state-stored root with a relational child collection and child lifecycle operations.

Select one concrete owning-module operation before implementation. Demonstrate a root with
owned child entities mapped to separate tables, a value object where it expresses actual domain
rules, and identity references to a separate aggregate. Establish the business meaning of the
root/children and the accepted/rejected operations before choosing Contracts or persistence rows.
Archived examples are evidence, not authorization to import their business rules.

The root controls creation and child add/change/remove behavior. Validate aggregate-wide
invariants on the complete proposed state. A child is not a separately writable aggregate merely
because it has its own table/key. References to other aggregates do not authorize modifying
their state through this root. Read-side joins/projections remain separate query choices.

Keep domain-to-row and row-to-domain adaptation explicit. Compare native EF mapping of the
domain model with separate row adapters, then choose the simpler fit for the selected operation.
Do not assume all aggregate shapes require separate persistence classes or a generic repository.

## Executable proof obligations

- Create/load the complete root and its children through actual consumer usage.
- Add/change/remove children while preserving child identity, ownership and the selected
  root-wide rules. Demonstrate rejected changes preserving accepted state.
- Prove root-version concurrency for child-only edits, including competing operations on
  different children. Independent child UPDATEs must not silently bypass a root-wide invariant.
- Fail a required root/child write and commit; verify atomic rollback and recovery in a fresh
  operation. Make child deletion/orphan handling deliberate rather than relying on accidental
  tracking behavior.
- Preserve tenant-bearing relationships and establish the selected other-aggregate references.
  Foreign keys, missing references and business eligibility are different obligations.
- Demonstrate a query that combines related information without bypassing write ownership.
  Query freshness requirements determine whether a direct join or maintained projection fits.

Use real PostgreSQL migrations and independently expected business results. No event sourcing,
domain-event dispatcher, broker or general mapping package is necessary to prove this capability.
If implementation exposes a repeated technical obligation, present its consumer interface and
proof scope before library promotion. Sample and documentation may be the complete outcome.

## Handoff

Report domain policy versus persistence mechanics, loading/update/deletion behavior, ownership,
concurrency and failure evidence. Record library, sample and template findings separately.
State explicitly when no new reusable mechanism was proven. D1's test results do not prove this
new relational child-collection protocol.
