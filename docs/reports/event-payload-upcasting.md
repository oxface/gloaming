# Explicit event payload upcasting

Date: 2026-10-07. Base checkpoint: `91542a6`.
Owner approved the interface and exact scope before implementation, including the provided
PassThrough helper. Complete changes remain unstaged for implementation review; no commit
approval is inferred from interface approval.

## Outcome and reusable mechanism

The optional, package-free Events.Serialization codec now reads explicitly declared historical
schemas through forward JSON transformations into registered CLR fact types. Construction
concentrates unique-source validation, complete-path resolution and terminal dispatch; decoding
concentrates ordered execution, JSON lifetime ownership and original-identity error reporting.
Consumers do not repeat these technical branches in history readers, stores or rebuilders.

New public surface is JsonEventUpcaster with immutable alias/source/target metadata, an abstract
Upcast method and a PassThrough factory. JsonEventCodec gains an optional third constructor
argument. Existing two-argument construction, registrations, Serialize/Deserialize and error
classification remain source-compatible. Existing distinct CLR types at different schemas
under one alias remain supported. No binary compatibility promise for already compiled callers
is made; this repository uses source project references.

The codec preserves one explicit write identity per concrete CLR type. Strictly increasing
same-name steps and unique sources prevent cycles/branching. Each declared source must reach
an exact terminal registration at construction. A source also registered as an exact schema
is rejected rather than given an order-dependent interpretation. Explicit skips are supported;
undeclared intermediate/future identities remain unknown. Serialize never upcasts.

PassThrough avoids a consumer subclass for compatible additive fields when the schema version
is bumped. Native optional defaults supply absent fields without changing the JSON. Keeping
the same schema for a compatible addition already works. Required fields and nullable/default
policy stay native; the helper does not certify semantic compatibility.

The original requested stored identity remains on decoding errors. JsonException from any
step or terminal decode becomes existing InvalidPayload; undefined/null upgrade elements also
fail. Other consumer failures and disposed elements propagate natively. Live inputs/results
are cloned so subsequent steps do not depend on earlier document lifetimes. Transforms must
be pure and concurrently usable; this is a consumer obligation, not runtime enforcement.

## Executable adoption and consumer policy

Inventory changes current receipt writes to schema 2 with receivedQuantity. Its module-owned
ReceiptV1ToV2 requires the old decimal quantity, renames it and preserves unrelated fields.
Domain Quantity, decisions, eligibility, reducers and inline-state shape remain unchanged.
Opening/issue schemas remain at 1. Existing history and independent rebuilding automatically
consume upgraded facts through their existing codec call, with no store/reader interface edit.
Ordinary inline loading does not replay or repair.

The standalone Purchasing codec adopter changes flat v1 fields to renamed flat v2, then nests
those facts in v3. Independent literal versions converge to the same current nested line and
preserve quantity 2.5, price 12.5 and total 31.25. Replacing quantity with 5 yields 62.50.
The existing domain fold is reused through a consumer-local conversion; no business rule
moves into the library. This materially different chaining/nesting obligation is independent
of EF, tenancy and the EventSourcing package.

Both executable programs have an opt-in --schema-evolution journey. Original default outputs
remain intact. The PostgreSQL journey seeds unchanged v1 fixtures, appends a current v2 receipt,
rebuilds with explicit native transaction/save/commit, compares complete retained event rows
and reads from a fresh scope. Its native process runs twice in the proof, exercising the flag
and sequential rerun behavior. It supplies no concurrent bootstrap guarantee.

## New verification

All test results below are fresh executions against this implementation, using SDK 10.0.112,
EF Core 10.0.12/Npgsql EF 10.0.3 and real PostgreSQL 18.6 under rootless Podman where needed.
No test was skipped.

| Suite | Passed | New cases in this slice |
| --- | ---: | ---: |
| EventSerializationTests | 49 | 33 |
| Standalone EventCodecDemo.Tests | 24 | 8 |
| Wholesale EventPersistenceDemo.Tests | 165 | 10 |
| EventSourcing family PostgreSQL | 67 | 0 |
| Independent raw EventStorageDemo.Tests | 35 | 0 |
| EventHistoryTests | 16 | 0 |
| ArchitectureTests | 68 | 0 |
| Total relevant tests | 424 | 51 |

Pure new cases prove one-/two-step and explicit-jump routes, intermediate sources, registration
order independence and collection snapshots, duplicate/shadowed/incomplete paths, forward
metadata, unknown/future versions, native strict fields/options, optional defaults, unchanged
source JSON, owned input/intermediate lifetime, disposed-result/non-JSON failures and concurrent
use with stateless transforms. Exact prior codec cases are retained.

PostgreSQL new cases prove mixed v1/v2 historical/current/temporal decoding and inline agreement
at the same version, preservation of the old delivery reference, an independently authored v2
literal, full rebuilding of current/missing/corrupt state, malformed old-payload rejection before
tracking, native repair-save failure and rollback, and fresh-context recovery. Complete event
rows (IDs, ownership, aliases, schemas, positions, timestamps and JSONB) compare unchanged
before/after repairs. Complete saved stream/state rows remain unchanged after rejected decode
or rolled-back/failed save. Existing append/repair competing-writer tests pass; this slice adds
no concurrency or lock mechanism. The old invalid-domain-sequence test copies schema_version
alongside the deliberately substituted fact so it still exercises domain sequence failure,
rather than an accidental payload/schema mismatch after receipt writes became v2.

The full 43-project solution builds with zero warnings/errors. CSharpier, active native style
and analyzer verification pass. The existing T1 proof creates Cedar and HarborDesk (namespace
Task) outside the checkout, restores/builds them and passes their 10 PostgreSQL adoption tests;
event-free omission and deterministic initial creation remain supported. The standalone native
executable prints its expected schema-evolution output. All 266 local documentation targets
in 14 changed/new Markdown files and whitespace checks pass. Logs are /tmp/upcasting-*.log
in this execution environment. The disposable T1 PostgreSQL container was removed after success.

Archive verification checks all 800 original files against the frozen manifest. All six retained
active v1 JSON files compare byte-for-byte against HEAD; new v2/v3 literals are authored additions.
No migration, stored-row update, archive source, business Contract, dependency, template or
unrelated edit is changed.

## Review-worthy files

- [Public upcaster](../../src/Gloaming.Events/Gloaming.Events.Serialization/JsonEventUpcaster.cs) and [codec orchestration](../../src/Gloaming.Events/Gloaming.Events.Serialization/JsonEventCodec.cs).
- [Pure codec proofs](../../src/Gloaming.Events/tests/EventSerializationTests/UpcastingTests.cs).
- [Inventory transform](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/ReceiptV1ToV2.cs), [codec registration](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCodec.cs) and [event annotation](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionEvents.cs).
- [Independent chained/nested adopter](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.Events/samples/EventCodecDemo/Purchasing/PurchaseOrderSchemaEvolutionExample.cs), [standalone journey](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.Events/samples/EventCodecDemo/SchemaEvolutionJourneys.cs) and [literal tests](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.Events/samples/EventCodecDemo.Tests/UpcastingCompatibilityTests.cs).
- [Native PostgreSQL journey](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/SchemaEvolutionJourney.cs) and [mixed-schema failure/recovery proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.Upcasting.cs).
- [Self-sufficient codec README](../../src/Gloaming.Events/Gloaming.Events.Serialization/README.md), [local capability record](../../src/Gloaming.Events/Gloaming.Events.Serialization/docs/capabilities.md) and [reviewed complete scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/event-payload-upcasting.md).

## Limits and historical evidence

Upcasting preserves domain facts in these two consumers, not arbitrary consumer transformations.
JSON numeric value is preserved; incidental textual decimal scale need not be. Stored rows are
never migrated. Compatible readers must be deployed before new-schema writers; old codecs cannot
decode new writes. No zero-downtime schema/meaning rollout, reverse conversion, alias rename,
event split/merge, repair worker, projection backfill, snapshot/catch-up, async or multi-stream
projection is supplied. Shape-only upgrades need not rebuild correct inline state; meaning
changes need a separately controlled rollout and rebuild decision.

The library does not enforce transform purity, authorization, tenancy, domain rules or transaction
completion. Consumers retain JSON policy, every transformation, reducers, schema/deployment
ownership and native final save/commit. EventSourcing EF continues to depend on History but not
Serialization; the direct-JSON counter remains independently adoptable. Template findings are
preservation of T1's event-free output, not an event preset.

Prior codec, native history, ES1/ES2 and bounded-reader reports retain historical checkpoint
results. The archive is compatibility evidence only. New proofs are the executions above;
existing concurrency/transaction mechanisms are newly rerun regressions, not newly extracted
mechanisms. One new reusable mechanism is proven: explicit historical payload-path decoding.
No further projection or maintenance capability follows from this slice.
