# Integration state transfer

Distilled from the 2026-10-09–22 research. [Evidence dates and limits](README.md) apply.
No maintained bootstrap sample or generic Gloaming feed API is supplied.

## Choose the receiver requirement first

A subscriber interested only in future business actions may need no initial population.
A sparse reference cache, complete searchable public dataset and historical business-effect
replay have different contracts. An outbox delivers selected committed intent; retained
outgoing rows alone do not provide a public snapshot boundary or indefinite replay history.
Private event-sourcing history is not automatically an integration contract.

| Receiver need | Alternative and cost to assess |
| --- | --- |
| A small subset of current references | Fetch/enrich on demand. Define cold-miss availability, bounded concurrent fetches, stale handling, deletion and races with incoming changes. |
| Complete current public state | Producer-owned consistent export plus retained overlapping changes; receiver population/readiness, restart and retention become explicit. |
| Rebuild from broker state | Evaluate an already operated retained/compacted state stream, including expiry and tombstones. Ordinary queues and finite retention do not imply complete current state. |
| Historical business effects | Define ordering and semantic idempotency for those effects; a latest-state merge is insufficient. |

## Proof obligations before extraction

For a complete-replacement state contract, a producer-owned per-key public revision can keep
an older import/fetch from overwriting a newer update. Equal revisions must agree; deletion
needs an equally durable revision/tombstone rule. Unrelated partial patches cannot safely be
discarded merely because another field arrived at a higher revision. Delivery ID, schema
version, public revision, event time and business-operation identity have different meanings.

A proposed export/overlap design must establish capture before a consistent source snapshot,
retain all relevant overlapping changes, and commit receiver state with local progress/receipt.
Prove update/delete overlap, late publication, duplicates, restart, partial population and
expired history. Reusing an old export requires its original change boundary and retained
history; a subscription created today cannot fill a missing intervening period.

Allocated sequence values and timestamps do not establish commit order or a safe global
cursor. An empty receiver queue can coexist with pending producer outbox work. Claims about
being current through a source commit need a coordinated barrier beyond per-key convergence.
Cross-aggregate atomic visibility is another separate requirement.

## Retired experiments and original evidence

B1's retained-feed experiment was rejected for adoption. B2's Dispatch directory was later
retired with sample reduction; disclosure, public-state revision, population and readiness
were consumer policy. Neither retirement removes the supported inbox/outbox route-selection
protocol. Revisit transfer only when a real receiver requirement justifies it.

Original [bootstrap research](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/integration-event-bootstrap-research.md),
[sparse hydration comparison](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/sparse-consumer-hydration-research.md),
[B1 assessment](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/b1-retention-and-contract-assessment.md) and
[retired Dispatch decision](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/adr/0022-dispatch-directory-complete-state-population.md)
retain alternatives and dated evidence. Primary references included
[Fowler's event-pattern distinctions](https://martinfowler.com/articles/201701-event-driven.html),
[AWS transactional outbox](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/transactional-outbox.html),
[PostgreSQL sequence behavior](https://www.postgresql.org/docs/18/functions-sequence.html) and
[logical decoding](https://www.postgresql.org/docs/18/logicaldecoding-explanation.html).
These comparisons select no CDC, broker, snapshot or replay engine.
