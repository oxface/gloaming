# D1.1: rich state-stored aggregate and relational children

Date: 2026-10-14. The owner agreed this follow-up scope while reviewing D1.
This is a later bounded increment, not an expansion of the current D1 implementation.
No new library interface or aggregate base is selected.

Implementation authorization: the owner's subsequent instruction to proceed selects this
next increment. It was prepared independently from fetched `origin/main` at `824c95b` in
`feat/rich-state-stored-aggregate`. At the owner's request, its working-tree changes were moved
onto `feat/ddd-sample-patterns` after D1 was committed as `041c64a`, retaining both demos.
[Executable usage](../../samples/Wholesale/RichAggregateDemo/README.md) and
[fresh verification/extraction findings](../reports/d1-1-rich-state-stored-aggregate.md)
are prepared for owner review.

## Selected sample behavior

Sales owns a state-stored draft order with relational line children. A trusted human caller
supplies the customer identity, currency, initial lines and an immutable order-total limit.
This limit is sample policy, not a customer credit engine or proof that a supplied price/limit
was commercially authorized. Accepted lines have stable per-order identities, a catalog SKU,
positive quantity and nonnegative unit price. The complete draft contains at least one line
and its representable total must fit that limit. A batch may temporarily remove all lines or
exceed the limit while constructing a candidate; only the complete final candidate is accepted.

Create a draft, then add/change/remove child lines through one explicit mutation batch. Stage
the complete accepted root/child graph in the owning native Sales transaction; the caller saves
and commits. Each mutation advances the root version exactly once, including child-only edits.
Native expected-version predicates arbitrate competing mutations on different children. A
tracked-save guard requires a complete loaded graph and root participation for child writes.

Customer existence/ownership is enforced in the owning Sales schema. Inventory catalog SKUs
are checked through Inventory.Contracts for creation and newly added lines, without a peer
implementation reference or cross-module database FK. Catalog availability is a read-time
dependency, not reservation, price validation or atomic eligibility at Sales commit. Existing
line terms remain captured draft data. Read summaries join the draft to the current Sales
customer name rather than copying another aggregate into the write model.

Compare direct EF mapping of a rich domain graph with separate relational rows. For this
immutable complete-candidate model choose explicit rows: tenant metadata, root version tokens
and FK/deletion staging remain visible, while the domain's Money value and child constructors
remain independent of EF materialization. This is a sample choice, not a mandatory adapter
layer or new mapping abstraction. Prove actual row round trips and child lifecycle behavior.

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
