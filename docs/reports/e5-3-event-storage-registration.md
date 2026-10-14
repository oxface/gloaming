# E5.3 explicit event-sourcing storage registration

2026-10-06. The owner-authorized [interface and scope](../plans/e5-3-event-storage-registration.md)
was owner-reviewed and checkpointed as `abcd370`, together with E5.2.2. This report records
implementation executions separately from checkpoint hooks; it makes no remote CI claim.

## Outcome and extraction finding

**A new reusable model-registration mechanism is proven** through two owning modules and an
independent native consumer. EventSourcing.EntityFrameworkCore configures consumer-owned header/
envelope rows, default columns, selected keys/relationship, a header version token, positive
versions and unique stream positions. Inventory and Purchasing replace their common mapping
with the utility, while keeping their own concrete row types and optional ownership fields.
Each formatted HistoryMapping shrinks from **79 to 42 lines**; its remaining code configures
Organization columns, explicit key expressions, ownership filters and PostgreSQL payload mapping.

The library references native EF Relational only. It has no other Foundry segment, provider,
hosting, codec, actor, tenant, messaging or sample dependency. Two technical row interfaces,
options and native registration extensions define the public surface. No separate abstractions
or PostgreSQL project was needed for one explicit jsonb consumer configuration call.

| Area | Finding |
| --- | --- |
| Library | Shared technical model registration removes duplicated configuration and works with ordinary and owned identities; metadata validation rejects unsafe key combinations. |
| Template | Consumer-owned row/provider/ownership registration and native migrations are exercised editable setup. Event sourcing remains optional; materialized output remains E10. |
| Sample | Both modules adopt the utility without SQL/schema changes. A separate executable uses custom tables, two StreamType values and an extra consumer column without other segments. |
| Consumer policy | Families, event meaning, encoding, identity scope, filters, expected-version preparation, narrow fault classification, transaction/saving, views, audit and publishing stay explicit. |

This corrects the earlier blanket description of mappings as consumer-owned: the technical
registration pattern earns extraction, while the module's storage/ownership choices remain
consumer source. Native EF still supplies concurrency and transactions. Generic append
orchestration is a separate candidate; no repository, scope wrapper or event collector was added.

## Registration and preserved behavior

### Naming follow-up, 2026-10-06

After reviewing the Marten/archive comparison, the owner approved proceeding with clearer
storage names. The interfaces are now IEventStreamRecord/IStoredEventRecord; the standalone
consumer uses EventStreamRecord/StoredEventRecord. Existing module concrete names remain
consumer-owned. The standalone migration designer/snapshot now reference the renamed CLR
types; migration SQL operations and durable table/column/constraint names did not change.

Fresh follow-up verification passed **157 cases**: standalone PostgreSQL storage **10**, module
PostgreSQL history/append **82**, and architecture **65**, with no failures or skips. The solution
build passed with no warnings/errors, and the standalone native pending-model check reports
no changes. These executions verify the naming revision; the broader E5.3 verification below
remains evidence from the preceding implementation run. No new test was added merely to check
the rename, and no new reusable mechanism was proven by it.

Formatting checked 293 files; native semantic style and analyzers passed. Active documentation
links/whitespace passed, and the archive verifier confirmed all 800 original files unchanged.
The sandbox initially denied the test/style runners' IPC sockets; the permitted reruns passed.

[E6.1's proposed scope](../plans/e6-1-inline-decision-state.md) covers explicit inline decision
loading and required views next. It remains a plan, not another implemented storage guarantee.

### Native model

The simple overload maps Id/EventId keys and StreamId references without tenancy. The explicit
overload forwards native scalar/composite key expressions to EF. Keys end in Id/EventId/StreamId
with corresponding optional ownership prefixes. Different consumer property names are permitted
on the two rows; event identity/reference must use the same event-row prefix and the reference
must target the selected header primary key. The unique position index derives from that actual
foreign-key metadata, so an ownership component cannot silently disappear from uniqueness.

The selected key, not StreamType, defines identity. In the sample, OrganizationKey/Id identifies
one stream across its module families. In the standalone consumer, Id is unique across both
families. StreamType remains query/decision metadata, not EF inheritance or family discovery.

The helper maps explicitly supplied GUID identities, the native header version token, positive
stream/event/schema checks and a restrictive envelope relationship. Consumer builders remain
available for extra fields or native policy replacement. Payload/provider mapping and ownership
are not populated by the library. Changing checked columns requires changing the native check
SQL; changing table/constraint names requires updating consumer fault classification where used.

Both module pending-model checks report no differences from their existing migrations. Concrete
row names/namespaces, columns, keys, constraints, indexes and histories remain unchanged. No
module migration was generated. Existing native query/append suites, ownership isolation and
constraint-specific conflict helpers pass through this new registration.

Unsupported ordinary-model shapes and invalid keys fail during construction. The helper uses
native metadata rather than an expression visitor, custom parser or runtime discovery. It adds
no request-time state, saving logic, StagedStreamIds bookkeeping, automatic collection or mapping
between domain, event-sourcing and integration events.

## Independent consumer and new proofs

[EventStorageDemo](../../samples/EventStorageDemo/README.md) references only this Foundry segment
and native EF/provider tooling. Its consumer rows map to **journal.streams/journal.facts**;
the header has a consumer-defined Description column. Explicit native migrations and transaction
ownership remain visible in the executable. Authored counter/note facts are standalone proof
data, not new Wholesale domain behavior.

Fresh-context reads independently report counter version 2/value **17** and note version 1/text
**review**. Actual schema inspection confirms only the one pair of data tables, with no per-family
tables. The executable is launched by the PostgreSQL suite and both output lines are checked.

The new **10 cases** comprise six PostgreSQL/executable cases and four focused model cases:

- Two stream types coexist in custom shared tables, with independently expected results and the
  extra Description column preserved. Actual consumer migrations run without pending migrations.
- Reusing a header identity with another type violates the selected key. Duplicate positions
  fail regardless of event name. Missing references fail and referenced headers cannot be deleted.
  Each failure leaves the authored stream type/version and envelope count unchanged.
- Two native contexts observe the same header version. A winner explicitly commits the next
  header/envelope fact; a stale header update fails with native DbUpdateConcurrencyException.
- Header-key ordering, omitted reference ownership and omitted event-key ownership fail during
  model construction. A matching Owner/Partition prefix with different consumer names succeeds.

The model cases protect the utility's key-validation choices without opening a database; they
are not a second general EF mapping matrix. Complete append/rollback/tenant behavior is reused
from the **82-case** module suite rather than duplicated in the storage demo. No fake store,
publisher effect counter, production hook or custom architecture graph tool was introduced.

## Fresh verification

All final affected suites passed with no failures or skips:

| Suite | Cases |
| --- | ---: |
| New independent storage consumer | 10 |
| Module PostgreSQL history/append consumer | 82 |
| PostgreSQL/HTTP consumer | 97 |
| Architecture | 65 |
| Independently adoptable codec/history consumer | 16 |
| **Total** | **270** |

Four new architecture cases protect declared/native dependencies and independent sample/library
boundaries. Existing Contracts/module checks include the new library. CI's active PostgreSQL
lane now includes EventStorageDemo.Tests; commit hooks remain container-free. No new toolchain,
package version or framework-only test infrastructure was needed.

The full **40-project** solution built with zero warnings/errors. Native style/analyzers passed
after the final test addition. CSharpier checked **293 files**, including the normalized new
consumer migration/snapshot. All three native pending-model checks report no changes. Archive
integrity verified all **800 original files** unchanged. Local documentation checking resolved
**708 links in 70 active documents**; tracked and new-file whitespace checks passed.

The initial restricted-delete assertion expected foreign_key_violation instead of PostgreSQL's
restrict_violation. Correcting that test expectation produced the passing final 10-case run;
relationship configuration did not need changing. Initial formatter checks caught missing
generated-file namespace spacing; the final checks include those files. Earlier failed runs
are not presented as final verification.

These are fresh local executions. Other core, PersistenceDemo, Aspire/browser and archived
architecture suites were not rerun; their results remain historical. Archived mappings informed
comparison, not evidence of this library's actual behavior or provider portability.

## Review and remaining gaps

Review [the row interfaces](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/IEventStreamRecord.cs),
[envelope interface](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/IStoredEventRecord.cs),
[options](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventSourcingStorageOptions.cs)
and [registration](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventSourcingStorageExtensions.cs).
Compare [Inventory configuration](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/HistoryMapping.cs)
and [Purchasing configuration](../../samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/HistoryMapping.cs)
with their concrete rows and unchanged snapshots. Then inspect [the independent context](../../samples/EventStorageDemo/StorageDbContext.cs),
[caller transaction recipe](../../samples/EventStorageDemo/DemoJourneys.cs), new native migration
and [database proofs](../../samples/EventStorageDemo.Tests/StorageTests.cs).

GUID/JsonElement ordinary rows and the documented key shape are the initial surface. Inheritance,
owned/shared entities, converted or arbitrary identities/payloads, type-scoped keys and other
providers are unproven. PostgreSQL **18.6**, EF **10.0.12** and Npgsql EF **10.0.3** are the actual
configuration. Native builder overrides are consumer policy, not a guarantee for every model.

Registration does not enforce append-only history against privileged SQL, provide safe retry
after ambiguous commit, establish business-key uniqueness or bound replay cost. E6 remains
required inline views and bounded repair. Audit, domain-event collection and integration-event
delivery remain separate explicit capabilities. Materialized template/bootstrap remains E10.
