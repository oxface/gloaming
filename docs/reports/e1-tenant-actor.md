# E1 implementation and proof report

Historical checkpoint report, 2026-10-03. The combined design below is superseded by
[the independent actor/tenancy split](e1-identity-split.md). Original implementation and
interfaces remain available at commit `a8e45c9`; the active [slice plan](../plans/e1-tenant-actor.md)
now describes the split. This report records the original work,
owner-reviewed and checkpointed as `a8e45c9`. Review revisions were left unstaged for the
owner's staging; the checkpoint followed explicit exact-change-set approval.

## Outcome

`ModulithFoundry.ExecutionIdentity` supplies opaque tenant
and actor identities, explicit anonymous/tenantless context, optional initiator attribution,
read/initialization interfaces, a disposable single-assignment holder, and typed requirement
checks. It has no package or project dependencies.

[The console sample](../../src/Gloaming.Tenancy/samples/ContextDemo/README.md) consumes the library with
explicit standard DI registration. Inventory returns independently seeded tenant quantities;
Sales calls its Contracts interface to assess a draft quantity. It also demonstrates system
versus human attribution and tenantless anonymous/identified execution. No production login
or durable business persistence is introduced.

The accessor uses one lock around initialization, reads and disposal. This keeps its state
transitions reviewable and publishes a complete immutable value. No lock-free performance
claim, ambient context, reset, nested restore protocol or automatic registration is added.

## Newly exercised guarantees

| Guarantee | Evidence |
| --- | --- |
| Keys reject absent/blank values and preserve exact identity text | Library tests cover null, empty, Unicode whitespace, casing, padding, different numeric spellings and Unicode normalization differences. |
| Actor kind distinguishes human/system identities; anonymity has no key | Factory/equality tests reject absent identified keys and distinguish equal human/system key text. |
| Tenant and actor choices are independent; attribution grants no identity | All six tenant-selection/actor-kind combinations, optional/anonymous initiator and separate/combined typed requirement failures are tested. |
| Context is established once without anonymous fallback | Public-interface tests cover early reads, invalid initialization, identical/different reassignment and competing initializers. |
| Parallel reads observe a complete immutable value | Sixteen readers each perform 100 reads; competing initialization accepts one complete context and rejects the other. These are finite concurrency scenarios, not throughput measurements. |
| Actual consumer DI wiring shares one holder and isolates operations | Composition tests use `DemoComposition`, overlap two scopes across awaits, and assert independent quantities 42/7 and actor identities. |
| Consumer policies are explicitly applied | Anonymous Inventory reads succeed with a tenant; missing tenant and anonymous Sales requests fail. A human initiator cannot satisfy the executing-actor requirement. |
| Scope termination invalidates the holder without invalidating captured values | Direct and DI tests exercise idempotent disposal, disposed reads/initialization, exceptions/cancellation and a fresh following operation. Independent child scopes need their own establishment. |
| Independent adoption has no unrelated runtime dependencies | Restored dependency graphs have zero core packages/references; the sample includes only the context project and Microsoft DI/DI abstractions. The sample builds and runs without web, EF, Rebus, Aspire or Access. |

These proofs exercise the new interface and consumer wiring. Archived findings are used as
comparison evidence, not counted as new test results.

## Verification

- Active four-project solution restore/build passed with zero warnings and errors.
- Library interface tests: 26 passed, none skipped.
- Sample composition tests: 14 passed, none skipped.
- Console completed with the documented quantities, fulfilment assessments and attribution.
- CSharpier checked 29 active C#/XML files; active semantic style and analyzer checks passed.
- Dependency verifier and archive verifier passed; all 800 original tracked files are intact.
- Workflow/hook YAML parsed successfully. Active documentation link/whitespace checks are
  included in the handoff validation.

Repeated after the owner-authorized naming/review revision: audited restore, zero-warning
build, all 26 library and 14 composition cases, console execution, 29-file CSharpier check,
semantic style/analyzers, dependency verification, 13 active documentation link checks and
archive verification passed. No active source/reference retains the old library name.
This revalidates E1 after the rename; it adds no HTTP or persistence guarantee. Existing
owner staging was preserved, including entries under the previous library path; the working
tree contained the unstaged rename and revised references for re-review. The owner then
reviewed/staged the complete change set and authorized the checkpoint. Commit hooks passed
all 40 active cases and 21 archived architecture cases; the architecture results check the
historical baseline, not physical module isolation in the new sample.

Test package `xunit.v3.mtp-v2` 4.0.1 and consumer DI 10.0.12 match the retained repository
SDK/package baseline and restored successfully with NuGet auditing enabled. The library
itself remains package-free. Test and Roslyn hosts used local IPC permissions; restore
used network access. No runtime assertion or audit was disabled. The accessor test class
suppresses only CA1859's concrete-type performance suggestion so those tests intentionally
exercise the two public interfaces.

CI now has a separate Active context lane for dependencies, style, analyzers, build, both
test projects and sample execution. Hooks include the active checks. Archived lanes retain
their existing behavior; container suites were not rerun for E1 because archived source
and runtime wiring were unchanged. CI execution itself awaits the normal repository run.

## Library, template and sample findings

The reusable mechanism now exercised is scoped, immutable context establishment with
single assignment, concurrent reads, disposal invalidation and explicitly invoked
requirement checks. It removes repeated context-holder lifecycle and validation code
while preserving consumer control. Shared technical identities do not require Access types.

The template contribution is the visible DI recipe in the working sample: one scoped holder,
reader/initializer aliases, complete initialization, awaited work and scope disposal.
It has one implementation to copy; no template generator or second sample is added yet.

Consumer-owned policies remain identity/provider resolution, account linking, tenant
admission, authorization, anonymous capability access, business rules, HTTP failure mapping
and attribution propagation. The sample's fixture lookup demonstrates consumer tenant
selection; it does not prove a general persistence isolation mechanism.

GUID-only identities, mutable scope rebinding and automatic fallback/attribution were
excluded by the agreed design. Tests exercise the corresponding identity/lifecycle
distinctions; this slice does not assess every alternative design's performance.

## Review order and limits

The owner-authorized re-review revision renames the runtime project/assembly/namespace to
`ModulithFoundry.ExecutionIdentity`. Solution, consumer/test references, dependency verification
and active documentation use the new name. Operation-context types and standalone adoption
remain the same; no new reusable mechanism is proven by this rename.

The first review adjustment assigns explicit enum numbers from 1, reserves 0 as invalid,
and adds `RequireTenantAndIdentifiedActor()` for the common combined requirement. Sales now
uses that call. This adds requirement ergonomics and a repository enum convention; it does
not establish a new isolation mechanism or enum database-mapping guarantee.

The subsequent HTTP strategy review records a template default of native authentication
requirements plus a required tenant, explicit exceptions, complete actor/tenant resolution
before initialization, and an editable OIDC registration helper in E3. Native authorization
owns HTTP caller access; no separate actor policy or actor endpoint extension is planned.
Authenticated-principal mapping must not silently downgrade failures to anonymous execution.
Core actor guards remain capability checks useful outside HTTP as well. This is the approved
direction for the later adapter/template increment; its interfaces and behavior are not
implemented in E1. No new reusable mechanism was proven by this documentation adjustment;
HTTP policy and ingress proofs remain pending.

Original review paths at checkpoint `a8e45c9`: `src/ModulithFoundry.ExecutionIdentity/`,
`tests/ContextTests/`, the console sample and its composition tests. These are historical
review pointers; current source and proof locations are in the split report.

The reader/initializer split expresses composition roles; hostile code in the same process
can still resolve or construct identities. Identity presence is not authentication or
authorization. Scope disposal prevents subsequent accessor use; it does not revoke already
returned values or stop work automatically.

EF isolation/write validation, production trusted ingress, durable attribution, messaging,
physical module isolation, host ServiceDefaults telemetry integration and template bootstrap
remain later proof gates. E2 persistence design is the next planning step after E1 review.
