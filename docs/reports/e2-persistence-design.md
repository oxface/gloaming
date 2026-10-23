# E2 persistence design findings

2026-10-03. Planning/evidence review only. The owner confirmed shared database, module schemas,
tenant-discriminated tables and rejection of tenant changes in ordinary persistence.
See [the complete E2 proposal](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e2-persistence.md) and
[the first-increment plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e2-1-tenant-ownership.md).
This report preserves the pre-implementation findings; subsequent implementation results
are in [the E2.1 proof report](e2-1-tenant-ownership.md).

## Outcome

Propose an explicit EF ownership model utility and tracked-change validator. Consumer
contexts, mappings, migrations, saves, commits and privileged workflows stay local. Storage
tenant keys need not depend on Tenancy or Access types.

The central new proof is a detached write carrying the current tenant key but a foreign
row ID. Tracked-value validation alone cannot identify the stored owner. Propose native
ownership concurrency predicates plus original/current-owner checks; PostgreSQL must prove
the combination before support is claimed.

## Evidence and verification

Read the archived shared filter, three module DbContexts, Customer/Order mappings,
representative persistence tests and architecture checks. Evidence links and limitations
are in the proposal. No archive file was changed or archived test rerun.

Official EF documentation confirms filter, concurrency-token, bulk-operation and transaction
semantics. Installed EF 10.0.12 XML documents named-filter/save overloads and `IsConcurrencyToken`.
`dotnet-inspect`'s matching guide was read; broad API inspection hit its retained-text limit,
and a local-library attempt could not resolve dependencies within the network sandbox.
Local package XML provided the exact signature evidence instead. This verifies available
APIs, not the proposed implementation's behavior.

No runtime code or new runtime test was added. No new reusable mechanism was proven.
E1's existing tests remain identity evidence only.

## Library, template and sample findings

Extraction candidates: ownership-filter construction/configuration and pending-write
validation, replacing repeated mechanics across contexts.

Template contribution: visible native save overrides, schema/history configuration,
ownership-aware constraints and migration setup. No library base context or transaction wrapper.

Consumer-owned: tenant mapping, global records, admission/permission, entity/business
constraints, version advancement, privileged operations, raw SQL/bulk policy, transactions
and provider choice. Transfer requires a separately designed consumer workflow.

## Review and gaps

Review the two signatures, metadata/concurrency effects, save wiring, model constraints,
supported-operation limits and proof matrix. The owner allowed native development-time
EF migration scaffolding; migrations remain reviewed consumer code. E2.1 proposes an
explicit EF Core Relational dependency to inspect unsupported relational mappings, without
requiring either actor identity or tenancy.
Advanced EF mapping support needs explicit proof rather than inheriting assumed protection.

Deliver each implementation with an executable consumer and relevant PostgreSQL proofs.
Shared cross-module transactions and trusted HTTP ingress remain separate increments.
