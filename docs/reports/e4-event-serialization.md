# E4 explicit durable event serialization

2026-10-06. Implemented the approved [E4 scope](../plans/e4-event-serialization.md) after
E3.7 checkpoint `dc3ac3b`, using the owner-selected `ModulithFoundry.Events.Serialization`
name. The owner approved the exact 42-file change set, checkpointed as `2a49ef3b`.
Pre-commit checks and commitlint passed. The hooks reran all 202 active container-free cases
and 21 archived architecture cases; these archived dependency checks are separate from
active product proofs and do not establish archived persistence or messaging behavior.

## Outcome and reuse finding

A new reusable mechanism is proven: explicit durable name/version registration, exact
runtime-type JSON dispatch and bounded decoding failures. One package-free library serves
two independently configured consumer families without an event base class, discovery,
domain policy or another Foundry segment. Removing it would restore registry/conflict/dispatch
code in both consumers, rather than merely removing a wrapper over `JsonSerializer`.

The archived Inventory and Purchasing serializers supplied evidence of duplicated technical
responsibilities. E4 reimplements those responsibilities with explicit registration instead
of attributed assembly scanning. Archived serializers, fixtures and checksums remain unchanged;
their earlier test results are historical, not fresh executions of this library.

The finite consumer reconstructs retained stock data with on-hand quantity **10.125** and
literal purchase-order data with total **31.25**. These independently expected business
results demonstrate actual use of decoded fields. They do not establish complete aggregates,
ordered stream hydration, business validity or persistence guarantees.

## Public surface and consumer obligations

The implementation exposes five types:

| Type | Responsibility |
| --- | --- |
| `EventRegistration<TEvent>` | Explicit concrete reference type and durable name/version; typed `For<TConcrete>` construction. |
| `JsonEventCodec<TEvent>` | Snapshot registrations/options, serialize exact runtime type and decode exact identity. |
| `SerializedEvent` | Ordinary name/version/payload output record; manually constructed envelopes are not validated. |
| `EventDecodingException` | Requested identity, bounded failure and optional native JSON cause. |
| `EventDecodingFailure` | Explicit `UnknownEvent = 1`, `InvalidPayload = 2`; 0 remains invalid. |

Conflicting identities or multiple write registrations for one CLR type fail at construction.
Nonblank names retain their exact text and compare ordinally; versions start at 1. An
unregistered derived type cannot inherit its base type's write alias. Unknown names and
unsupported versions fail before inspecting payload; there is no version fallback or skip.

Known identities with undefined payload, a null decode result or native `JsonException`
produce `InvalidPayload`. The outer error message excludes payload data. Native inner causes
retain diagnostics, so logging remains consumer-owned. Other converter/configuration faults
keep their native exception type. Null arguments, malformed source JSON text, disposed
source documents and envelope extraction are caller concerns.

Native JSON options are copied and made read-only before use, and registrations are copied
into private dictionaries. Changing the source options/list cannot rebind an established
codec. Converter/resolver objects are shared references: their mutable state, lifetime and
concurrent-use suitability remain consumer obligations. There is no deep-clone or blanket
thread-safety promise for arbitrary converters.

The typed factory has one method-local CA1000 suppression. Its generic family scopes the
registration and its generic method selects the concrete event type. This retains the small
typed interface without a separate factory facade or global analyzer disable; review the
choice with the factory itself.

## Library, template and sample findings

| Location | Finding and ownership |
| --- | --- |
| Library | Reusable registry, exact dispatch, option snapshot and failure boundary. Platform `System.Text.Json`; no package/project/extra-framework references or I/O. |
| Template material | Editable explicit registration and native JSON recipe in the consumer. No additional template generator/resource is needed here; materialized template output remains E10. |
| Inventory sample | Owns item/location/unit/quantity, `JsonRequired` policy and an optional delivery reference. Renamed CLR `StockPositionReceived` reads retained `inventory.stock-position.received` v1 JSON. |
| Purchasing sample | Owns supplier/currency/lines/totals and enables required constructor parameters. Its registry and JSON policy are independent of Inventory. |
| Consumer boundary | Owns fixture I/O, envelope parsing, sequence choice, local folds and output. Stream metadata, domain validation and product-specific failure context stay outside the codec. |

The two Inventory files are byte-for-byte copies of retained fixtures. Purchasing literals
are newly authored from the archived payload contract, not claimed as retained historical
files. [Provenance](../../src/Gloaming.Events/samples/EventCodecDemo/Fixtures/README.md) records the
distinction. Active builds/runtime use local copies without archive references.

The two internal example families live in the standalone consumer pending E5 module
integration. They do not add an empty Purchasing Contracts project or a full HTTP order
workflow. Existing state-stored Inventory, Sales, Access and HTTP/AppHost behavior is unchanged.

## Fresh verification

All nine container-free suites passed, with no failures or skips:

| Suite | Cases |
| --- | ---: |
| Architecture | 51 |
| ActorIdentity | 19 |
| Tenancy | 17 |
| ContextDemo | 15 |
| EF model/validation | 23 |
| ActorIdentity HTTP | 15 |
| Tenancy HTTP | 39 |
| Event serialization | 16 |
| EventCodecDemo | 7 |
| **Total** | **202** |

The 23 new codec/consumer cases exercise registration ambiguity, interface writes, exact
lookup, invalid payload classification, native configuration snapshots, converter-fault
propagation, retained literals, required/optional contracts, separated registries and actual
console output. They avoid a native JSON option matrix, framework-only assertion tests,
private reflection or implementation snapshots.

Two architecture cases extend existing native-XML/ArchUnitNET policies. The six technical
libraries' dependency posture is checked without a restored-project parser or exact
transitive-package whitelist. The actual standalone consumer build includes only the codec
and executable project; the full **31-project** active build also passes with zero warnings
or errors. Both fresh builds passed, as did native style/analyzer verification.

CSharpier verified **216 files** including generated source. Archive integrity verified all
**800 original files**; both copied Inventory fixtures also matched their originals byte for
byte. All four active fixture JSON files and both CI/hook YAML files parsed successfully.
The local Markdown checker resolved **564 links in 58 active documents**; whitespace checks
passed and the Git index remained empty.

Container-free CI now runs both new suites and the console; hooks run the two suites.
This records local execution and configured CI coverage, not an observed remote CI run.
The unchanged 143 database/runtime/browser cases remain historical E3.7 evidence; E4 did
not rerun those suites or the archived behavior suites. E3.7's 320-case result must not be
presented as a fresh E4 all-project result.

## Review files and remaining boundary

Start line-by-line review with the [registration factory](../../src/Gloaming.Events/Gloaming.Events.Serialization/EventRegistration.cs)
and [codec](../../src/Gloaming.Events/Gloaming.Events.Serialization/JsonEventCodec.cs), then the
[output record](../../src/Gloaming.Events/Gloaming.Events.Serialization/SerializedEvent.cs),
[decoding exception](../../src/Gloaming.Events/Gloaming.Events.Serialization/EventDecodingException.cs)
and [failure enum](../../src/Gloaming.Events/Gloaming.Events.Serialization/EventDecodingFailure.cs).
The [library guide](../../src/Gloaming.Events/Gloaming.Events.Serialization/README.md) describes
the consumer obligations.

Review the actual [Inventory recipe](../../src/Gloaming.Events/samples/EventCodecDemo/Inventory/StockPositionExample.cs)
and [Purchasing recipe](../../src/Gloaming.Events/samples/EventCodecDemo/Purchasing/PurchaseOrderExample.cs)
with [library proofs](../../src/Gloaming.Events/tests/EventSerializationTests/CodecTests.cs) and
[consumer proofs](../../src/Gloaming.Events/samples/EventCodecDemo.Tests/CompatibilityTests.cs).
The [architecture changes](../../tests/ArchitectureTests/AssemblyDependencyTests.cs),
[CI](../../.github/workflows/ci.yml) and [hooks](../../lefthook.yml) accompany the capability.
Checkpoint-document updates record E3.7's already approved commit; they introduce no new
E3 behavior.

Reference event types, one write identity per type and exact registered schema versions
define this first boundary. Multiple read schemas per CLR type, upcasting, other JSON engines,
AOT and transport compatibility remain unproven. `SerializedEvent` is not a schema/domain
validator; native required/null enforcement is a consumer contract with platform limits.

E5 is the next boundary after review: module-owned event types, ordered history hydration,
expected-version append and caller-controlled persistence. Required views/repair are E6;
reliable delivery is E7. E4 performs no transaction, append, retry, projection, publication
or automatic domain-event collection.
