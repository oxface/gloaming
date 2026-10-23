# E1 actor identity and tenancy split report

2026-10-03. Implemented under owner authorization, owner-reviewed and checkpointed as
`c8cbf64` after explicit approval of the complete 55-file staged change set. This revises
checkpoint `a8e45c9`, whose combined interface and 40-test result
remain [historical E1 evidence](e1-tenant-actor.md). See [the revised interfaces](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e1-tenant-actor.md).

## Outcome

Two package-free libraries replace `ModulithFoundry.ExecutionIdentity`:

- [ActorIdentity](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/README.md) owns actor kind/key,
  explicit anonymity, optional initiator, immutable actor context, reader/initializer,
  single-assignment holder and identified-actor requirement.
- [Tenancy](../../src/Gloaming.Tenancy/Gloaming.Tenancy/README.md) owns tenant key, explicit selected
  tenant or tenantless context, reader/initializer, single-assignment holder and tenant
  requirement. It requires no actor or authentication model.

Neither references the other or a common runtime package. Holder implementations remain
small and local to each library. Public requirement failures are independent typed
exceptions; the shared discriminator and combined library guard are removed. This is a
breaking revision of the inline E1 interface, with the active consumers migrated together;
there is no compatibility facade retaining the previous coupling.

[The console sample](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/src/Gloaming.Tenancy/samples/ContextDemo/README.md) exercises actor-only
host-information/maintenance composition, tenancy-only Inventory availability and combined
Sales preview. Sales visibly requires both contexts and calls Inventory through Contracts.
The same application actor can operate in separate scopes for different tenants.

Each holder still publishes its whole immutable value once. Combined publication is no
longer atomic: the consumer owns completion of the segments a capability requires before
calling it, and aborts/disposes on establishment failure. Actor identity may be available
for tenant-admission lookups before tenancy is initialized. Sales rejects either missing
segment; arbitrary actor/tenant membership consistency remains consumer policy.

## Fresh proofs and verification

| Guarantee | Current evidence |
| --- | --- |
| Independent adoption | Restored assets show no packages/project references or additional framework references in either library. Actor and tenancy executable test projects each reference only their selected runtime segment. |
| Independent composition | Actual sample registration omits tenancy in actor-only composition and actor identity in Inventory composition; those operations succeed. |
| Required composition | Sales rejects either uninitialized segment. Six tenant-choice/actor-kind combinations preserve explicit consumer requirements, including anonymous Inventory reads and identified tenantless rejection in Sales. |
| Immutable scoped lifecycle | Both test consumers cover early reads, null initialization, identical/different reassignment, competing initializers, 16 readers with 100 reads each, and idempotent disposal before/after establishment. |
| Identity and attribution | Exact keys, Unicode/case/padding differences, human/system key equality, anonymous actor, optional/anonymous initiator and rejection of initiator as executing identity. Actor enum values remain 1/2/3; 0 invalid. |
| Operation isolation | Concurrent scopes use the same actor with tenant quantities 42/7 across awaits. Child scopes inherit neither context. Exceptions/cancellation dispose both holders; the following operation requires fresh establishment. |

Fresh runs on the six-project active solution:

- Audited restore passed; build passed with zero warnings and errors.
- Actor-identity tests: 21 passed; tenancy tests: 19 passed; composition tests: 23 passed.
  Total 63, none failed or skipped.
- Console completed with the expected 42/7 quantities, fulfilment assessments and attribution.
- Restored dependency verification passed, including separate test consumers and the DI-only sample.
- CSharpier checked 38 active C#/XML files successfully.
- Archive verification passed: all 800 original tracked files remain unchanged.
- Active semantic style and analyzer verification passed. Local links in 18 active/supporting
  documents, CI/hook YAML parsing and diff whitespace checks passed.

The default parallel build host failed in this environment, including a CLR crash; the
single-process build with build servers disabled succeeded. Build/test/formatter-host checks
used local IPC permissions; restore used network access for the existing audited package
baseline. No runtime assertion or audit was disabled. CA1859 remains narrowly suppressed
in public-accessor tests to exercise reader/initializer interfaces deliberately.

CI and hooks now run both standalone library suites and composition proofs. Archived lanes
are unchanged; archived container suites were not rerun for this split. CI execution itself
awaits the normal repository run. These finite concurrency tests establish the exercised
lifecycle scenarios, not a throughput or performance claim.

## Library, template and sample findings

Newly proven adoption behavior is that actor identity and tenancy can each be consumed
without the other's runtime dependency, registration or establishment. The reusable lifecycle
mechanism was established in original E1 and is freshly exercised for both independent
holders; the split adds no new persistence, authentication or authorization mechanism.

The template contribution remains visible standard-DI setup and operation lifetime in the
sample, now with three explicit compositions. There is no generated template or additional
registration framework. Inventory fixtures and Sales rules remain sample code; technically
requiring a selected tenant or identified actor does not establish membership or permission.

Membership, external-identity mapping, authorization, candidate selection and canonical
resolution, admission, failure presentation and attribution propagation stay consumer-owned.
Organization denotes the sample's domain/UI grouping; Tenant denotes the technical isolation
boundary. Optional HTTP utilities for configurable route values/hostnames and custom resolver
strategies remain E3 proposals, alongside a separate Access membership/admission increment.

## Review-worthy files and remaining gaps

1. [ActorContext](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/ActorContext.cs),
   [actor holder](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/ActorContextAccessor.cs), reader/
   initializer, guard and typed failure in that directory.
2. [TenantContext](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContext.cs),
   [tenant holder](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContextAccessor.cs), reader/
   initializer, guard and typed failure in that directory.
3. [Consumer registration](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/src/Gloaming.Tenancy/samples/ContextDemo/DemoComposition.cs),
   [operation establishment](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/src/Gloaming.Tenancy/samples/ContextDemo/Program.cs),
   [Sales requirements](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/src/Gloaming.Tenancy/samples/ContextDemo/Sales/DraftOrderPreview.cs)
   and tenancy-only Inventory consumption.
4. [Actor lifecycle proofs](../../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/AccessorTests.cs),
   [tenant lifecycle proofs](../../src/Gloaming.Tenancy/tests/TenantTests/AccessorTests.cs),
   [composition proofs](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/src/Gloaming.Tenancy/samples/ContextDemo.Tests/CompositionTests.cs),
   [current dependency architecture tests](../../tests/ArchitectureTests/AdoptionDependencyTests.cs),
   solution, CI/hooks and revised documents.

EF isolation/write validation remains E2 design work. Membership, native HTTP authentication/
authorization, tenant selection middleware and production trusted ingress remain E3 work.
No route/hostname strategy is implemented here. Durable attribution, messaging, physical
module isolation and template bootstrap retain later proof gates. At the split handoff, the
existing E2 proposal/report remained planning documents; the split only updated their sample
accessor naming. Subsequent persistence results are recorded in [the E2.1 report](e2-1-tenant-ownership.md).
The initial handoff was unstaged. The owner subsequently staged and approved the exact
55-file set; checkpoint `c8cbf64` includes the split and the existing E2 planning documents.
Commit hooks passed 63 active tests, 21 archived architecture tests, formatting, active and
archived style/analyzers, dependency verification and commitlint. The archived test results
remain historical-baseline checks, not new library guarantees.

The checkpoint used the original Python dependency verifier. It was subsequently replaced
by [active .NET architecture tests](architecture-tests.md); checkpoint results above remain historical.

[The subsequent active test audit](test-audit.md) records the current 51 context tests.
The original 63-case checkpoint result above remains historical evidence.
