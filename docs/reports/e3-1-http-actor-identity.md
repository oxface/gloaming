# E3.1 actor identity in native ASP.NET Core requests

2026-10-04. Implemented after the owner approved [the interface and scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-1-http-actor-identity.md),
following E2.4 checkpoint `6069c05`. Owner review, including the registration refinement,
concluded with approved checkpoint `faefc0b` on 2026-10-05.

## Outcome and reusable mechanism

The optional ActorIdentity ASP.NET Core adapter establishes the immutable core context from
the effective native request principal. An explicitly wrapped evaluator establishes after
policy authentication and before consumer authorization handlers; downstream middleware
covers requests without a policy. Both paths share one successful request publication.
The inner native evaluator still authenticates and authorizes; consumers own mapping and
HTTP failure presentation. The package-free ActorIdentity and Tenancy cores are unchanged.

**A new reusable mechanism was proven:** context establishment across native policy-selected
authentication and policy-free requests, without duplicating policy selection, adding actor
requirements or inferring provider claims. Removing this adapter would return ordering,
single publication and failure handling obligations to each HTTP consumer. It references only
the core and native framework; standalone proofs have no sample, EF, Tenancy or OIDC package.

The design direction is in [ADR 0002](../adr/0002-explicit-http-actor-establishment.md).
The interface consists of the consumer resolver, evaluator wrapper, completion middleware
and typed mapping failure, with explicit opt-in service/pipeline extensions added following
owner review. There is no Options collection, automatic scheme wiring, AsyncLocal holder,
mediator or runtime generation.

## New executed evidence

Fifteen standalone HTTP cases use protected native cookie tickets, a consumer-selected inner
evaluator, native policies and a test-only secondary authentication scheme:

| Guarantee | Evidence |
| --- | --- |
| Native default/fallback composition | Valid credentials map to independently expected application keys. Missing credentials preserve native cookie challenges and do not run mapping or business work. |
| Policy-free completion | A public endpoint without fallback policy maps a valid cookie once; a subsequent credential-free request observes explicit anonymity. |
| Native anonymous/permissive policies | Anonymous metadata and a named policy allowing anonymous access observe anonymity. Authenticated callers on the public endpoint retain their mapped actor. |
| Effective selected scheme | With an Alpha default cookie and Beta selected scheme, both a consumer authorization handler and the endpoint observe Beta. Minimal API policy metadata and routed `[Authorize(AuthenticationSchemes = ...)]` are exercised. |
| Selected-scheme failure | Failure of the selected scheme cannot reuse Alpha's default actor. The authorization handler observes anonymity, business work does not run and the selected scheme's challenge survives. |
| Mapping failure is not anonymity | Unknown authenticated identities and an anonymous resolver result stop protected/public work. This consumer chooses 422, demonstrating that response policy is outside the adapter. |
| Attribution grants no permission | The mapped actor is available inside authorization, but failed native permission still produces the cookie's forbid response and prevents business invocation. |
| Request isolation | Two operations deliberately overlap after establishment, retain Alpha/Beta identities and invoke mapping once each. A following anonymous request retains no old identity. |
| Cancellation | Cancel an awaiting resolver. The request propagates cancellation with no published actor or endpoint invocation and no retry; a fresh request succeeds. |
| Immutable identity and delegation | Principal replacement after establishment is rejected. The explicitly selected inner evaluator is called for native authentication/authorization. A consumer-supplied system actor and human initiator survive publication unchanged. |

Six separate HTTP sample cases call the real `DemoComposition`, apply its configured native
OIDC claim actions, protect cookie tickets and execute the sample endpoints. The same subject
and email under two configured issuers maps to distinct application users; unknown issuers
and ambiguous subjects receive the sample's 403 problem response. Public anonymity and
authenticated public identity are retained. Tests explicitly select cookie-only challenge
behavior to avoid contacting an external provider; production configuration selects OIDC.

Two findings changed the exercised consumer/proof wiring:

- Native cookie challenges/forbids in the standalone text-endpoint configuration are redirects;
  typed JSON API endpoints in the sample's cookie-only proof return 401. Initial test
  assumptions about these responses were corrected; the adapter does not normalize them.
- Native OIDC defaults delete `iss`, breaking the sample's issuer/subject resolver. Running
  those actual configured actions made both known-user cases fail with 403. The editable
  sample now removes that deletion action; all six cases pass. This proves the chosen claim
  configuration, not remote issuer/signature validation or callback traffic.

The architecture suite now has 24 cases: eight additional compiled dependency prohibitions
cover the new adapter/consumer, and one native-XML declaration case admits only ActorIdentity
and `Microsoft.AspNetCore.App` for the adapter. The existing core and EF declaration policies
remain intact. No restored-graph parser or transitive dependency snapshot was introduced.
Both new HTTP suites are wired into the container-free CI lane and commit hooks.

## Initial E3.1 verification (2026-10-04)

All nine active suites passed: **161 cases, no failures or skips**, including **42 real
PostgreSQL cases**. These are initial E3.1 executions; E2.4's 131 counts remain historical.

| Suite | Passed |
| --- | ---: |
| ActorIdentity | 19 |
| Tenancy | 17 |
| Context consumer | 15 |
| Actor HTTP adapter | 15 |
| HTTP sample composition | 6 |
| EF ownership model/interface | 23 |
| Architecture/model/migration policies | 24 |
| Wholesale PostgreSQL consumer | 36 |
| Independent GUID PostgreSQL consumer | 6 |

The complete 16-project active solution builds with zero warnings/errors. Required semantic
style and analyzer checks pass; CSharpier checks 104 files including generated artifacts.
Archive checks preserve all 800 original files;
no archived runtime suite was rerun. Whitespace and changed-document local links are checked.
The provider login/callback was not executed and is not counted as new evidence.

A separate smoke run started the actual sample entrypoint under Kestrel on a dynamic
loopback port with a placeholder authority and no credentials. `/health` returned
`"healthy"`; `/public-identity` returned `{"kind":1,"actorId":null}`. The process shut down
cleanly. This is executable startup/public-request evidence, not another automated test case
or a provider login proof.

## Registration refinement (2026-10-05)

The owner requested extension methods to simplify consumer registration. The opt-in
`AddHttpActorContext<TResolver>()` groups the existing scoped holder/aliases/resolver and
transient evaluator wrapper. Its factory overload explicitly selects a custom inner evaluator;
it does not discover or decorate earlier registrations. `UseHttpActorContext()` adds completion
middleware where the consumer calls it, after native authorization. Both helpers are called
once; authentication configuration, policies, pipeline order and failure presentation remain
consumer-owned. Direct composition remains available.

The executable sample uses the native-default overload; standalone HTTP proofs use the
custom-evaluator overload. Existing tests now enter through these public helpers rather than
repeating their wiring. No registration-descriptor assertions or framework-only tests were
added. The private request feature and core holder lifecycle remain unchanged.

New verification: the complete active solution builds with zero warnings/errors using
`dotnet build ModulithFoundry.slnx --no-restore --disable-build-servers -m:1`. The actor HTTP
suite passes 15 cases, sample composition passes 6, and architecture passes 24: **45 cases,
no failures or skips**. CSharpier checks 19 adapter/sample/proof files; solution-wide semantic
style and analyzer verification and whitespace checks pass. PostgreSQL and the
other foundation suites were not rerun for this registration-only refinement; their counts
above belong to the initial E3.1 execution. The local test runner requires named-pipe access
outside the restricted sandbox. No provider callback or Kestrel smoke was rerun.

Library finding: useful explicit wiring utilities, with no new dependencies or runtime
protocol. Template finding: the exercised recipe now uses these smaller calls. Sample policy
stays in its existing authentication, directory and failure code. **No additional reusable
runtime mechanism was proven by this refinement.** Live provider and tenancy/membership gaps
remain as listed below. At the review handoff this refinement was unstaged; it is included
in the owner-approved checkpoint.

## Checkpoint verification (2026-10-05)

All normal commit hooks passed for the exact approved 43-file change set. They reran
repository formatting (105 files), active and archived semantic style/analyzers, seven
active container-free suites (**119 cases**) and the archived architecture suite (**21 cases**),
plus commit-message validation. These 140 cases passed without failures or skips.
This checkpoint execution does not rerun the PostgreSQL suites, Kestrel smoke or OIDC provider
traffic; their evidence and remaining limits stay as recorded above. The worktree was clean
after the commit.

## Review order and library/template/sample findings

Review [the resolver](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/IHttpActorContextResolver.cs),
[registration/pipeline helpers](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/HttpActorContextExtensions.cs),
[failure type](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/HttpActorResolutionException.cs),
[evaluator](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/ActorContextPolicyEvaluator.cs),
[middleware](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/ActorContextMiddleware.cs) and
[shared establishment implementation](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/HttpActorContextEstablishment.cs)
line by line. The last file owns cancellation checks, authenticated/anonymous branching,
principal-reference validation and publication after successful core initialization.

Then review [sample registration/pipeline](../../samples/Wholesale/Api/DemoComposition.cs),
[native authentication](../../samples/Wholesale/Api/NativeAuthentication.cs),
[current actor resolver](../../samples/Wholesale/Api/HttpIntegration/ApplicationActorResolver.cs),
[standalone proofs](../../src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/HttpActorTests.cs) and
[sample proofs](../../samples/Wholesale/IntegrationTests/Api/CompositionTests.cs).

E3.3 replaced the original configured directory/resolver with persisted Access. The E3.1
fixture source remains at `faefc0b`; the current link above points to its
[documented replacement](e3-3-persisted-access.md), not new E3.1 execution evidence.

Library: reusable request establishment and typed mapping failure. Template: the exercised,
editable native registration/pipeline and cookie/OIDC recipe in
[the sample guide](../../samples/Wholesale/Api/README.md). Sample policy: known
issuer/subject directory, one authenticated identity, application-user mapping, generic 403
response, default/fallback authentication requirements and endpoint selection. These do not
enter the library. No separate generated template file or bootstrap CLI was introduced.

## Remaining gaps

Only the documented explicit request composition is supported. Principal replacement is
detected by reference; mutation in place is not inspected. Identity-changing error-route
re-execution, nested authentication, remote provider login/callback/session revocation and
complete BFF/antiforgery behavior remain unproven. OIDC configuration is consumer-owned.

The sample directory is an immutable fixture, not durable Access or a membership model.
Tenancy selection/admission, Organization membership/revocation, permissions, business module
HTTP calls and native Aspire topology remain separate E3 increments. The next proposed slice
is the independently adoptable tenancy HTTP adapter;
[its proposed interface](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-2-http-tenancy.md) needs separate review.
