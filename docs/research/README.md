# Research behind the library boundaries

The four original briefs distill repository research conducted on 2026-10-06–22. External
documentation and pinned source were inspected then; they were not fetched again for that consolidation.
Treat version-specific findings as dated evidence and recheck them before adopting a new
engine or protocol. Recommendations are inferences for Gloaming/its consumers, not additional
supported features or implementation authorization.

| Question | Brief |
| --- | --- |
| Who owns authority, complete permission queries and database isolation? | [Authorization and tenant isolation](authorization-and-isolation.md) |
| When do local reactions or durable module coordination help? | [Domain and module composition](domain-and-module-composition.md) |
| How can a new consumer acquire existing public state? | [Integration state transfer](integration-state-transfer.md) |
| Which framework patterns are useful without importing a framework? | [Native persistence and delivery](native-persistence-and-delivery.md) |
| What additional bounded mechanisms might real Umbrine needs justify? | [Wolverine/Marten capability comparison](wolverine-marten-opportunities.md), fresh primary-source research on 2026-10-23. |

The newer capability comparison records its own source scope and Umbrine working-tree evidence.
Its ranked experiments are proposals, not supported Gloaming features or an implementation queue.

The [current design](../design.md), [extraction gate](../plans/library-extraction.md),
[ADRs](../adr/README.md) and family-local contracts govern current behavior. Marten,
Wolverine, MassTransit and other systems are comparison evidence, not a feature-parity target.
Detailed source matrices, counterexamples and execution diaries remain in the
[pinned report tree](https://github.com/oxface/modulith-foundry/tree/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports).
No new reusable mechanism or runtime proof is established by this distillation.
