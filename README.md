# Modulith Foundry

Gloaming is a set of opt-in .NET libraries usable independently in APIs and workers.
This repository develops them against an executable modular-monolith sample and provides
a consumer-owned application template. Consumers own their application composition, module
policy, transactions, transport routing, and worker deployment.

The reusable libraries use `Gloaming.*` namespaces, project names and assembly names.
The repository, sample applications and template retain their existing identities.
See [the rename report](docs/reports/gloaming-library-renaming.md) for the scope and verification.
Umbrine is the future ERP product built on this template and Gloaming; the names share a darkness theme.

Library families own their package source, documentation and library tests. Each package is
independently selectable; a family folder does not add an umbrella dependency.

| Family | Packages |
| --- | --- |
| [ActorIdentity](src/Gloaming.ActorIdentity/README.md) | Core and optional ASP.NET Core adapter |
| [Tenancy](src/Gloaming.Tenancy/README.md) | Core and optional ASP.NET Core adapter |
| [Persistence](src/Gloaming.Persistence/README.md) | EF Core ownership utilities |
| [EntityFrameworkCore](src/Gloaming.EntityFrameworkCore/README.md) | Model-selected preparation and validation through common save composition |
| [Events](src/Gloaming.Events/README.md) | Independent serialization and history utilities |
| [EventReactions](src/Gloaming.EventReactions/README.md) | Composable event buffers, ordered reactions and optional EF preparation |
| [EventSourcing](src/Gloaming.EventSourcing/README.md) | Aggregate core and optional EF Core write store |
| [Messaging](src/Gloaming.Messaging/README.md) | Incoming/outgoing envelopes, EF inbox/outbox and optional workers, PostgreSQL delivery |

The [state-stored template rehearsal](docs/plans/t1-template-rehearsal.md) is owner-approved
and checkpointed as `8ccf4c8`. Its [creator](tools/template/README.md) generates an independent
Catalog/console repository with configurable naming and existing library source
snapshots, now including shared EF infrastructure required by Persistence, without domain-event or messaging dependencies. [The report](docs/reports/t1-template-rehearsal.md)
records the supported creation behavior and limits.

[The separate worker host](samples/MessagingWorkerDemo/README.md) demonstrates API-independent
dispatch, broker intake and local processing using existing Messaging APIs. Explicit finite
setup applies module migrations and declares the queue; real process proofs cover competing
workers, graceful stopping, abrupt death and fresh-process recovery.

The [local reservation workflow](samples/Wholesale/LocalWorkflowDemo/README.md) delivers through
public module intake Contracts without a broker. Existing inbox/outbox workers retain independently
committed decisions and Sales-owned compensation; PostgreSQL/process-death proofs cover the handoff.

[ES1 bounded event append](docs/plans/es1-library-write-store.md), checkpointed as `ab85ec9`, adds reviewed aggregate bookkeeping
and a provided IEventStore write coordinator to the optional native EF event segment.
State-dependent Inventory issues and an independent history/direct-JSON counter exercise it.
Explicit native save guards validate registered required-state participation. Consumers keep
domain rules, view definitions and native save/commit. Existing event experiments and T1's event-free output
remain intact. See [the store slice report](docs/reports/es1-library-write-store.md) and
[the current plan](docs/plans/library-extraction.md).

The original wholesale sample and its documentation are preserved under
[`archive/proof-sample`](archive/proof-sample). They supply behavioral evidence and known
limits for deliberate reimplementation, rather than prescribing the new library design.
The first active segments are independent [actor identity](src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/README.md)
and [tenancy](src/Gloaming.Tenancy/Gloaming.Tenancy/README.md), exercised by [the finite wholesale context sample](samples/Wholesale/ContextDemo/README.md).
The first [EF ownership utility](src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/README.md)
was checkpointed with an executable Inventory consumer. The
[two-module persistence sample](samples/Wholesale/PersistenceDemo/README.md) now owns native
Inventory/Sales migrations and separate histories checkpointed as `f2dcf2b`.
Same-tenant customer/address relationships were checkpointed as `d67c7fc`.
Versioned profile changes with caller-owned transactions were checkpointed as `6069c05`,
completing the initial E2 persistence scope. The optional
[actor HTTP adapter](src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md) and
[HTTP identity sample](samples/Wholesale/HttpIdentityDemo/README.md) were reviewed and
checkpointed as `faefc0b`. The independently adoptable
[tenancy HTTP adapter](src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/README.md) and Organization-scoped
catalog sample were reviewed and checkpointed as `cfbac9a`, including explicit selection
presets and a native host-filter options utility. Persisted Access lookup and membership
admission were checkpointed as `20a02be` in [E3.3](docs/plans/e3-3-persisted-access.md), with
[real PostgreSQL evidence](docs/reports/e3-3-persisted-access.md).
[E3.4](docs/plans/e3-4-persisted-business-ingress.md) was checkpointed with E3.5 as `31c7a8b`: admitted
requests read tenant-owned Inventory data through separate module/Contracts projects, with
[actual database and architecture proofs](docs/reports/e3-4-persisted-business-ingress.md).
[E3.5](docs/plans/e3-5-profile-mutation.md) adds protected, versioned Sales profile edits.
[E3.6](docs/plans/e3-6-runtime-composition.md) adds an editable
[Aspire AppHost](samples/Wholesale/AppHost/README.md), explicit setup, readiness and native
telemetry, checkpointed as `28ee797`. [E3.7](docs/plans/e3-7-oidc-browser-journey.md) implements
optional local Keycloak, explicit application account mappings and real browser journeys,
checkpointed as `dc3ac3b` with [implementation findings](docs/reports/e3-7-oidc-browser-journey.md).
[E4](docs/plans/e4-event-serialization.md) implements the independent
[Events.Serialization library](src/Gloaming.Events/Gloaming.Events.Serialization/README.md), immediately
used by a [two-family consumer](samples/Wholesale/EventCodecDemo/README.md), with
[fresh proofs](docs/reports/e4-event-serialization.md) checkpointed as `2a49ef3b`.
[E5.1](docs/plans/e5-1-event-history.md) adds independently adoptable
[ordered-range validation](src/Gloaming.Events/Gloaming.Events.History/README.md) with explicit two-family
hydration, owner-reviewed and checkpointed as `4cc12a1`. [Its report](docs/reports/e5-1-event-history.md)
separates memory-only proofs from database guarantees.
[E5.2.1 native EF reads](docs/plans/e5-2-1-native-event-history.md) adds owning Inventory/Purchasing
modules and an [executable database consumer](samples/Wholesale/EventPersistenceDemo/README.md),
owner-reviewed and checkpointed as `4f4d5b2`. [Its report](docs/reports/e5-2-1-native-event-history.md)
records PostgreSQL selection/isolation/capture proofs. The owner-reviewed
[E5.2.2 append slice](docs/plans/e5-2-2-native-event-append.md) adds explicit command staging
and caller-owned transactions before views and messaging. [Its report](docs/reports/e5-2-2-native-event-append.md)
records real PostgreSQL conflict/rollback proofs; no new library mechanism was needed.
[E5.3 storage registration](docs/plans/e5-3-event-storage-registration.md) adds
[EventSourcing.EntityFrameworkCore](src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md),
adopted by both modules and an [independent shared-table consumer](samples/EventStorageDemo/README.md).
E5.2.2 and E5.3 were checkpointed together as `abcd370`;
[the storage report](docs/reports/e5-3-event-storage-registration.md) records
fresh tenant-free/customized/mixed-stream proofs and preserved module schemas.
[E6.1](docs/plans/e6-1-inline-decision-state.md) implements inline decision state and
required views in both modules, checkpointed separately as `1ae13d4`. [Its report](docs/reports/e6-1-inline-decision-state.md)
records atomic view updates and editing without history replay; no new library mechanism was proven.

- [Current design decisions](docs/design.md)
- [Project glossary](CONTEXT.md)
- [Library and sample extraction plan](docs/plans/library-extraction.md)
- [E1 tenant/actor slice proposal](docs/plans/e1-tenant-actor.md)
- [E1 split implementation and proof report](docs/reports/e1-identity-split.md)
- [Original E1 checkpoint report](docs/reports/e1-tenant-actor.md)
- [E2 first-increment plan](docs/plans/e2-1-tenant-ownership.md)
- [E2.1 implementation and proof report](docs/reports/e2-1-tenant-ownership.md)
- [E2.2 module migrations plan](docs/plans/e2-2-module-migrations.md)
- [E2.2 implementation and proof report](docs/reports/e2-2-module-migrations.md)
- [E2.3 tenant relationships plan](docs/plans/e2-3-tenant-relationships.md)
- [E2.3 implementation and proof report](docs/reports/e2-3-tenant-relationships.md)
- [E2.4 versioned profile changes plan](docs/plans/e2-4-versioned-profile-changes.md)
- [E2.4 implementation and proof report](docs/reports/e2-4-versioned-profile-changes.md)
- [E3.1 actor HTTP integration proposal](docs/plans/e3-1-http-actor-identity.md)
- [E3.1 implementation and proof report](docs/reports/e3-1-http-actor-identity.md)
- [E3.2 tenancy HTTP integration proposal](docs/plans/e3-2-http-tenancy.md)
- [E3.2 implementation and proof report](docs/reports/e3-2-http-tenancy.md)
- [E3.3 persisted Access and admission proposal](docs/plans/e3-3-persisted-access.md)
- [E3.3 implementation and proof report](docs/reports/e3-3-persisted-access.md)
- [E3.4 persisted business ingress and module projects plan](docs/plans/e3-4-persisted-business-ingress.md)
- [E3.4 implementation and proof report](docs/reports/e3-4-persisted-business-ingress.md)
- [E3.5 Sales profile mutation plan](docs/plans/e3-5-profile-mutation.md) and [proof report](docs/reports/e3-5-profile-mutation.md)
- [E3.6 runtime composition plan](docs/plans/e3-6-runtime-composition.md) and [proof report](docs/reports/e3-6-runtime-composition.md)
- [E3.7 real OIDC/browser journey plan](docs/plans/e3-7-oidc-browser-journey.md) and [proof report](docs/reports/e3-7-oidc-browser-journey.md)
- [E4 event serialization plan](docs/plans/e4-event-serialization.md) and [proof report](docs/reports/e4-event-serialization.md)
- [E5.1 ordered history plan](docs/plans/e5-1-event-history.md) and [proof report](docs/reports/e5-1-event-history.md)
- [E2 persistence proposal](docs/plans/e2-persistence.md)
- [E2 design findings](docs/reports/e2-persistence-design.md)
- [Active .NET architecture checks](docs/reports/architecture-tests.md)
- [Active test value audit](docs/reports/test-audit.md)
- [Development and verification](docs/development.md)
- [Repository workflow](docs/conventions/repository.md)
- [Archive provenance and commands](archive/README.md)

## Development

The SDK, formatter, editor settings, Lefthook, and Conventional Commit tooling remain
pinned in the repository. Install the existing repository tools with:

```bash
dotnet tool restore
npm ci --prefix tools/repository
npm exec --prefix tools/repository -- lefthook install
```

Run the active sample with:

```bash
dotnet run --project samples/Wholesale/ContextDemo/ContextDemo.csproj
```

Every push to main and PR selects relevant checks from changed paths and their native
project dependencies. Documentation-only changes run lightweight repository checks; code
changes select affected Gloaming families, consumers and generated-template adoption. The
[CI lane map](docs/development.md#ci-lanes) lists their responsibilities. The
[sample runtime workflow](.github/workflows/sample-runtime.yml) tests Aspire startup and real
browser login on PRs changing Wholesale implementation or shared build/CI inputs, or by manual request.
Archived builds/tests are excluded from CI and commit hooks; their files remain available
as historical reference material, with frozen-source checksums verified by CI.
