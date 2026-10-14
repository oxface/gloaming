# D1: state-stored and event-sourced domain decisions

Date: 2026-10-14. Implementation prepared for owner review on `feat/ddd-sample-patterns`,
from fetched `origin/main` at `824c95b`. Scope: [D1 plan](../plans/d1-ddd-sample-patterns.md).
No library promotion, commit or push is inferred from implementation authorization.

## Outcome

The [DddDemo executable](../../samples/Wholesale/DddDemo/README.md) exercises two existing
sample models. Sales creates its one-address customer profile through finite setup, commits
a valid edit and rejects an invalid address. Purchasing drafts an order, rejects an overflowing
line total without tracking a partial proposal, then commits a valid line with total 37.50 EUR.
Both are read back through existing business Contracts; Purchasing also has explicit history.

Sales now owns an internal immutable CustomerProfileAggregate and validated CustomerProfileDetails
value. Creation calculates a complete valid profile before either seed row is tracked. Profile
changes validate proposed text, load state, reject a mismatched observed version and build a
complete candidate before copying values into native rows. Native expected-version predicates
remain necessary for commits racing after the preflight read. Every accepted edit increments
the profile version, even when text is unchanged.

The existing CustomerProfiles service still commits its own native transaction. Customer,
address and explicit audit are atomic; a valid domain candidate is not evidence of durability.
The new aggregate contains no EF types, actor/tenant context, event list or inherited technical
base. Trusted-human admission, ownership, schema constraints, row adaptation and audit stay in
the owning service/module. Stored profiles must satisfy its identity, version and text rules;
the new loading path is not a corruption-repair operation.

Purchasing code and its domain rules are unchanged. The sample uses its existing aggregate,
decisions, reducers, event store and caller-owned transaction. It illustrates that candidate
validation happens after the complete batch, so a temporary large line can be replaced before
acceptance. Invalid batches preserve accepted state, version and previously pending facts.
Historical replay and command eligibility remain separate existing responsibilities.

## Extraction decision

**No new reusable mechanism was proven.** Reusing the reviewed EventSourcedAggregate proves
continued consumer usage, not a new D1 library mechanism. A state-stored immutable candidate
does not repeat its event-position/pending-fact obligations, so a shared aggregate base would
add coupling without removing the same correctness work.

Customer code/identity, the one-address boundary, text limits and profile details are sample
policy. Purchase-order quantity, price, replacement and total rules likewise belong to Purchasing.
The shared concepts of identity and value equality do not by themselves justify a generic ID,
value-object package or repository. Native row adaptation/transactions remain explicit recipes.
No DDD package, default persistent domain events, automatic public-event mapping or new library
interface was extracted.

Library finding: existing optional event bookkeeping fits the event-sourced consumer; no new
technical surface is needed. Sample finding: immutable state replacement and complete-event-batch
acceptance offer distinct useful executable patterns. Template finding: the patterns are editable
source candidates; T1's generated event-free template is unchanged and no D1 preset was tested.
Any future promotion needs repeated concrete behavior and owner interface review.

## Verification

Fresh executions on 2026-10-14:

| Check | Result |
| --- | --- |
| Whole active solution build | Passed, zero warnings/errors. |
| D1 proof suite | 22 passed, zero failures/skips: 16 pure domain cases and 6 real PostgreSQL consumer cases. |
| Existing HTTP profile/admission/audit suite | 105 passed, zero failures/skips. Includes competing edits, trusted admission, audit/commit faults and cancellation. |
| Native event consumer suite, excluding broker DispatchTests | 172 passed, zero failures/skips. |
| Persistence/migration suite | 36 passed, zero failures/skips. |
| Architecture suite, including DddDemo composition boundary | 82 passed, zero failures/skips. |
| Active solution style/analyzer verification | Passed. The sandbox blocked the build-host pipe; the same read-only checks passed with local pipe access. |
| CSharpier | Passed; 603 active files checked. |
| Documentation links and whitespace | 247 local links in all 7 changed/new Markdown files resolve; Git whitespace checks pass. |
| Archive integrity | All 800 frozen originals match the manifest. |
| Review snapshot | Index entries hash remains `dbcbb4ef004f30b907635166c4e77477c7fc645100cb169f7169625d7519b423`; no staged changes. |

The PostgreSQL tests use Testcontainers PostgreSQL 18.6, rootless Podman and disposable databases.
They run the executable journey, independently assert committed profile/order values and history,
verify unchanged native models, reject invalid profile input before tracking, retain the winner
after stale/future expected versions, and prove rollback after the customer save when address
persistence fails. Recovery uses a fresh operation scope. Pure tests cover exact value equality,
text/identity guards, unchanged observed Sales state and checked version overflow; Purchasing tests
exercise rejected quantities/prices/totals and complete-batch replacement with pending facts.

The wholesale CI lane runs the new proof assembly. Its path map includes the executable/tests;
existing module changes also trigger native event and messaging lanes. Architecture coverage
includes DddDemo as a composition host that modules, Contracts and libraries cannot depend on.
Only the dedicated D1 proof assembly has internal access to Sales/Purchasing domain types.

The new tests invoke DddJourney directly, rather than launch a separate OS process. They establish
the finite consumer/database behavior, not process death, deployment, throughput or production
admission. Prior slice results and archived fixtures are context only, not counted as new D1 proof.
The 417 passing tests above were executed for this slice; broker/process, Aspire/browser and
template-generation suites were not rerun. No changed runtime behavior requires a new broker
topology or template composition proof.

## Review-worthy files and remaining gaps

- [Sales domain model](../../samples/Wholesale/modules/Sales/Sales/CustomerProfileAggregate.cs):
  profile details, identity/version construction, immutable replacement.
- [Profile service](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs) and
  [finite creation](../../samples/Wholesale/modules/Sales/Sales/SalesDemoSeed.cs): domain/persistence
  adaptation, early version conflict and preserved native save/audit ownership.
- [Consumer journey](../../samples/Wholesale/DddDemo/DddJourney.cs) and
  [composition](../../samples/Wholesale/DddDemo/DddComposition.cs): distinct transaction recipes,
  explicit finite identities and rejected-candidate recovery.
- [Domain proofs](../../samples/Wholesale/DddDemo.Tests/DomainDecisionTests.cs) and
  [PostgreSQL proofs](../../samples/Wholesale/DddDemo.Tests/ConsumerTests.cs): independent expectations
  and rejection/rollback evidence.

Production profile creation, a richer customer/address lifecycle and business-key admission are
not delivered by finite seeding. Cross-module atomicity, persistent ordinary domain events, CQRS
dispatch, B2 state transfer and T2 template materialization remain separate scopes. The new pure
Sales model does not make its current module assembly independent of messaging dependencies.
Library sources/interfaces, business Contracts, migrations, archived originals and template
files are unchanged. All D1 edits remain unstaged for review; preserve the existing index.
