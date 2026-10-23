# E3.2 HTTP tenant establishment

2026-10-05. The owner approved [the scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-2-http-tenancy.md), including middleware
after native authorization and actor completion. Implementation, consumer usage and proofs
were owner-reviewed and checkpointed as `cfbac9a`, including the registration refinements.
The owner approved the complete 50-file change set; no files were added to that approved
set during checkpointing. The preceding actor adapter checkpoint is `faefc0b`.

## Outcome and reusable mechanism

`ModulithFoundry.Tenancy.AspNetCore` establishes an asynchronously resolved canonical,
admitted tenant before endpoint work. Its configurable required-by-default policy and native
endpoint/group/controller metadata remain independent of native authentication policies.
Explicit tenantless results are distinct from resolution/admission failure. Publication uses
the existing core's one-assignment scoped holder; both foundation cores remain unchanged.

This slice proves a new reusable mechanism: independently adoptable HTTP tenant establishment
with explicit consumer admission and requirements. Optional named-route and bounded-subdomain
utilities also have executable consumer use. It adds no actor dependency, common context
framework, evaluator, membership model, database integration or retry/response policy.
The declaration permits only Tenancy and `Microsoft.AspNetCore.App`; compiled dependency
rules and the actual tenancy-only test consumer protect that boundary.

## Fresh evidence

Four focused suites ran against the active implementation on 2026-10-05:

| Suite | Passed | Evidence |
| --- | ---: | --- |
| TenancyAspNetCoreTests | 39 | Canonical publication, requirements/overrides, native gate ordering and selected schemes, optional candidate helpers, faults/cancellation and concurrent scopes without ActorIdentity. |
| ActorIdentityAspNetCoreTests | 15 | Existing actor-only HTTP behavior and independent adoption remain valid. |
| HttpIdentityDemo.Tests | 17 | Existing six identity cases plus same-actor Organization selection, anonymous Inventory data, hostname composition/failures, native host filtering and a capability guard outside HTTP. |
| ArchitectureTests | 35 | 28 forbidden compiled dependency relationships, five direct declaration policies and two unchanged sample model/migration policies. |
| Total | **106** | No skipped or failed cases in the final execution. |

Meaningful red/green steps first exposed missing publication, absent requirement enforcement,
ignored metadata, silent unknown-selection downgrade, the unimplemented hostname helper and
missing cancellation/principal/unmatched-route guards. Sample requests initially had no
Organization endpoints. The completed proofs assert context, failures, absent business work
or independently expected stock quantities through public seams.

The native pipeline cases protect adapter ordering and publication. They do not independently
assert that cookies, DI, metadata or native authorization work. No service-descriptor snapshots,
private-state reflection, assertion-engine fault tests, restored-project graph parser or
transitive-package whitelist was added. Candidate text preservation has a direct public-method
check; invalid base domains now fail preset registration before request work.

The 18-project active solution restores and builds with zero warnings/errors. Required
semantic style and analyzer verification pass, as does repository CSharpier verification.
Archive checksum verification and whitespace checks pass. The archive is unchanged.

A separate native **Kestrel** smoke run used the compiled executable with fixture Organization
configuration and a placeholder OIDC authority/client, without provider requests. `/health`
returned `"healthy"`; anonymous North/South catalog reads returned canonical
`wholesale-alpha`/`wholesale-beta`, explicit Anonymous kind 1, null actor ID and independently
expected quantities **42/7**. A fresh run after refinement also returned the consumer's
404 `application/problem+json` response for an unknown Organization. The host was stopped
normally. This is a runtime smoke result,
not a remote-login proof.

No PostgreSQL, RabbitMQ, archived behavioral or Aspire topology suites were rerun here.
E2's 42 PostgreSQL cases and E3.1's earlier broader runs remain historical evidence for
unchanged persistence, not tests of this fixture catalog.

The checkpoint hooks subsequently passed all **180 active tests** across eight container-free
suites and **21 archived architecture tests**, plus root formatting, active/archived semantic
style and analyzers, and commit-message validation. These are fresh hook results, not
PostgreSQL, remote-provider or archived business behavior results.

## Behavior proven and approaches rejected

- Required defaults reject absence before publication/business work. Anonymous, permissive
  named and authenticated fallback-policy requests retain the separate tenant requirement.
- Endpoint exceptions, nested groups in both directions, same-level ordering and routed
  controller/action attributes use one native metadata contract. Allowed tenantless work
  still retains a valid selected tenant and rejects malformed/unknown selection.
- Native challenge/forbid never calls the tenant resolver. Custom user-to-tenant selection
  sees the policy-selected principal rather than the default-scheme identity, without either
  actor library or candidate helper.
- Route slugs map to canonical keys. Subdomain selection respects its explicit base domain,
  case/port/terminal dot, apex absence and single-label boundary; wrong suffixes, nested
  labels and IP hosts fail. Raw forwarded headers do not replace the selected host.
- Null under required or tenantless-allowed requirements publishes nothing. Admission and
  unexpected exceptions propagate unchanged, with no retry. Cancellation before/during
  asynchronous resolution and principal replacement prevent publication/business work.
  Fresh requests after faults/cancellation succeed; overlapping tenant scopes remain separate.
- Unmatched endpoints retain native 404 without resolution. Undefined requirement 0 fails
  native options startup validation. Core one-assignment semantics remain the lifecycle rule.

Rejected: a fixed route shape, automatic strategy fallback, treating unknown/denied selection
as absence, relying on native fallback authorization for tenant enforcement, or coupling
Tenancy to ActorIdentity. This first slice also rejects adding a second native evaluator just
to make tenant-aware authorization handlers work; that requires its own consumer and plan.

## Library, template and sample findings

**Library:** resolution/publication, requirement metadata/registration and bounded candidate
extraction earned reuse. The consumer chooses defaults, resolver, selection strategy,
canonicalization, admission and presentation. Candidate helpers never grant access. Native
host/proxy policy is not installed by the adapter. Principal-reference checks do not detect
in-place claims mutation; the host must keep identity and routing stable. Context segments
are not published or rolled back atomically together.

**Template:** the library and sample READMEs contain the exercised registration/pipeline,
requirements/exceptions and route/Subdomain selection recipes. Consumer-owned native OIDC,
Organization directory, failure handling and host configuration remain editable. Reviewed
registration presets and an explicit native host-options utility now reduce setup.
Materialized template files and bootstrap CLI remain
E10; there is no generated template to apply yet.

A sample integration failure established a concrete host recipe detail: native filtering
rejected the terminal-dot request before selection. Explicit apex/wildcard entries for both
terminal-dot forms fixed the real composition. The adapter did not normalize or weaken native
filtering. Native matching ignores ports and compares case, but the demonstrated allow-list
must include the dot forms. See [native HostString matching](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Http/Http.Abstractions/src/HostString.cs)
and [host filtering](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Middleware/HostFiltering/src/HostFilteringMiddleware.cs).

**Sample:** Access owns configured Organization lookup/fixture admission and selection.
Inventory owns fresh catalog fixtures and `IStockCatalog`/`StockAvailability` Contracts; the
host reads through that Contract. Known Organizations are admitted to these demonstration
reads; no durable membership is claimed. The same globally mapped application actor selects
two Organizations in separate requests. Anonymous reads still have a selected tenant.
Folders establish ownership within the existing small host, not assembly-level module isolation.
Inventory repeats its tenant guard for non-HTTP calls. Fixtures do not prove database isolation.

## Registration and global-error refinement

The owner requested common selection presets and explicit registration-time strategy choice.
`AddRouteTenancy<TResolver>` and `AddSubdomainTenancy<TResolver>` now register candidate
selection and scoped lookup/admission via `IHttpTenantCandidateResolver`. Its null candidate
means absence; null result still means failed resolution/admission. The existing full-request
`IHttpTenantContextResolver` remains available for custom selection. All paths share the
same holder/options/middleware, with no actor dependency or strategy discovery.

The two presets initially supplied no candidate and failed 13 existing HTTP cases. Wiring
the actual selectors restored canonical publication, failure handling and scope isolation.
The same 39-case suite now exercises ordinary route/subdomain behavior and asynchronous
fault/cancellation through presets; custom user selection exercises the full-request escape
hatch. No registration-descriptor or native-framework-only cases were added.

`UseTenantSubdomainHosts` is an opt-in native `HostFilteringOptions` utility. It validates the
base domain and replaces the allow-list with apex/wildcard patterns and terminal-dot forms.
The consumer calls native `AddHostFiltering` explicitly; selecting subdomain tenancy does
not implicitly install filtering/proxy policy. Wildcards permit nested hosts at the native
filtering step; candidate selection still rejects them. This proves a reusable setup utility,
not membership or proxy trust. Invalid subdomain presets now fail during registration.

The sample removes `OrganizationSelection`, uses public Access registration helpers to keep
its implementation types internal, and makes endpoint patterns explicit in startup.
`AddServices` replaces the misleading `AddIdentityServices` name. Organization lookup remains
a fixture pending the data-backed Access slice.

The consumer's `ContextExceptionHandler` now uses native `IExceptionHandler` and
`IProblemDetailsService`, with `AddProblemDetails` and direct `UseExceptionHandler()` handling.
Known statuses/titles remain consumer policy; unrecognized faults use native 500 fallback.
No error route re-executes context establishment. Existing sample cases now assert actual
ProblemDetails status/title and media type for the mapped errors. The same **106** focused
cases pass after refinement, plus the 18-project build, style, analyzers, formatter and
archive verification. Native Kestrel confirms catalog reads and the mapped 404 response.
See [native exception handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).

## Review-worthy files and gaps

Review public interface/outcomes and policy first:

- [IHttpTenantContextResolver](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/IHttpTenantContextResolver.cs),
  [candidate lookup/admission interface](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/IHttpTenantCandidateResolver.cs),
  [requirements/options/attribute](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/TenantRequirementAttribute.cs)
  and [typed failure](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/HttpTenantResolutionException.cs).
- [Middleware](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/TenantContextMiddleware.cs),
  [candidate helpers](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/HttpTenantCandidates.cs)
  and [explicit registration/metadata helpers](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/HttpTenantContextExtensions.cs),
  plus [native host-options utility](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/HttpTenantHostFilteringExtensions.cs).
- [Consumer composition](../../samples/Wholesale/Api/DemoComposition.cs),
  [Organization resolver](../../samples/Wholesale/Api/HttpIntegration/OrganizationTenantResolver.cs),
  Organization directory fixture (retained at `cfbac9a`, since replaced by
  [persisted Access](../../samples/Wholesale/modules/Access/Access/ApplicationAccess.cs) in E3.3),
  [Access registration](../../samples/Wholesale/Api/HttpIntegration/OrganizationTenancyExtensions.cs),
  [explicit startup](../../samples/Wholesale/Api/Program.cs),
  [global exception mapping](../../samples/Wholesale/Api/ContextExceptionHandler.cs)
  and [Inventory capability](../../samples/Wholesale/modules/Inventory/Inventory/StockCatalog.cs).
- [HTTP proofs](../../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/HttpTenantTests.cs),
  [failure/lifecycle proofs](../../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/EstablishmentTests.cs),
  [native pipeline proofs](../../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/NativePipelineTests.cs),
  [hostname proofs](../../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/HostSelectionTests.cs)
  and [sample composition](../../samples/Wholesale/IntegrationTests/Api/CompositionTests.cs).

The implementation and refinements were owner-reviewed before checkpointing.
[E3.3 proposes persisted Access lookup/admission](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-3-persisted-access.md); a tenant-bearing
state-stored business journey using E2 persistence follows separately. Tenant-aware native authorization handlers,
revocation/invitations/roles, provider/proxy infrastructure, BFF/session/antiforgery, mutation
semantics, full module project structure and shared transactions remain unproven here.
