# E3.7 Real OIDC and protected browser journey

Status: owner-reviewed and checkpointed as `dc3ac3b` after E3.6 checkpoint `28ee797`.
All configured pre-commit and commit-message hooks passed. See
[the scope](../plans/e3-7-oidc-browser-journey.md) and
[local runtime guide](../../samples/Wholesale/AppHost/README.md).

## Outcome and ownership

The existing native Aspire graph optionally hosts a disposable HTTPS Keycloak 26.8.0 realm.
External OIDC configuration and provider-free public usage remain available. Hosting stays
on 13.5.4; the optional sample integration is explicitly pinned to
`Aspire.Hosting.Keycloak` 13.5.3-preview.1.26425.3. Playwright 1.63.0 and matching Chromium
are test-only. None of the five technical library projects changes.

The realm has a confidential authorization-code/PKCE client and three demonstration users.
Passwords/client secrets arrive through secret parameters and environment placeholders;
no real credentials are committed. Registration, password grants and service accounts are
disabled. Native certificate handling supplies provider TLS and API backchannel trust.
The API retains native discovery, token validation, PKCE and callback handling.

The manually started finite setup accepts the actual realm issuer and imported subjects,
mapping Alpha/Beta to the existing global application users. It rejects partial, insecure or
duplicate-subject inputs before migrations or seed writes. Existing standalone setup calls
retain fictional defaults when all three inputs are absent. Authentication requests never
create or link accounts, and matching email grants no application identity.

Default authentication/challenges use native cookies; `/login` explicitly challenges OIDC.
This corrects a production/test mismatch: the previous HTTP fixture alone replaced an OIDC
default challenge with cookies. That override is removed. Recognized JSON APIs now return
native 401 instead of redirecting to the provider in the executable host as well as tests.
No custom challenge middleware or new actor authorization policy is introduced.

The first automated browser run exposed an additional metadata gap: the profile GET and
antiforgery GET returned untyped `IResult`, so their anonymous browser fetches followed the
login redirect rather than receiving API status codes. Those handlers now use native
`TypedResults` (a typed result union for profile reads), which supplies JSON API metadata.
The browser proof detects this consumer contract error without XHR headers or custom cookie
events. Existing HTTP cases had covered other typed endpoints and JSON-body mutations, not
these two anonymous GETs.

## Integration findings

A default endpoint expression evaluated inside Keycloak translated the API callback host
to `aspire.dev.internal`. The browser used localhost, so the provider rejected the actual
login with `invalid_request`/`Invalid parameter: redirect_uri`. The callback now uses native
`KnownNetworkIdentifiers.LocalhostNetwork` and the exact allocated HTTPS `/signin-oidc` URI.
Real login then completed without a wildcard callback, admin provisioning worker or
backchannel certificate bypass. This is a consumer resource-network choice, not a new
Foundry library mechanism.

Keycloak storage is ephemeral so each start imports the current exact callback. Normal local
use selects a stable provider port for retained Access mappings; isolated/test graphs use
fresh databases and randomized ports. Issuer changes require explicit consumer account
mapping. Startup does not rewrite retained identities or reconcile seed data.

Readiness remains a table-presence/connectivity check. It can become healthy while finite
setup is still seeding, so the guide and E3.6 report now explicitly distinguish setup exit 0
from API readiness. Browser tests wait for both; no setup coordinator or readiness mutation
is added.

## New evidence

Three actual Chromium journeys use independently disposable native Aspire graphs,
PostgreSQL, the imported realm, real provider forms and native callbacks. Follow-up requests
use browser cookies and same-origin `fetch`; no authentication cookies are injected or OIDC
form posts replayed by tests.

- Alpha resolves to the expected application human actor and selects both Organizations
  through independent requests. Protected anonymous APIs return 401 without redirecting.
  Missing antiforgery returns 400 without a write; a real cookie/token pair permits an edit,
  advances the version and persists the independently expected profile. The other tenant's
  profile remains unchanged; a stale expected version returns 409 without another write.
- Beta is admitted to South and denied North. A deliberately unavailable Sales table makes
  ordering observable: denied admission still returns 404 before business persistence.
  Suspending membership through fixture SQL rejects the next South operation with the same
  native session cookie, while the tenantless application identity remains Beta.
- The unmapped provider user completes login but receives application mapping failure 403,
  including on public identity/catalog endpoints. Its email matches Alpha, but Access retains
  exactly the two explicitly seeded users and external links.

Three finite-command PostgreSQL cases verify rejected explicit setup inputs leave the module
tables absent. They protect validation-before-mutation ordering rather than .NET argument
validation. Existing detailed native cookie, admission, persistence, race and cancellation
proofs remain in the HTTP suite; their whole matrices are not repeated in the browser suite.
The provider-free runtime setup/database-outage case remains independently exercised.

A separate manual native CLI run discovered the actual HTTPS endpoints, explicitly started
setup and completed Alpha login through the Keycloak form in Chromium. Browser requests
returned `application-alpha` with `wholesale-alpha` and `wholesale-beta`; a protected edit
returned 200 and a later read observed changed text and version 1 becoming 2. Native
`aspire stop` stopped the disposable graph. This observation is separate from automated
test counts and the earlier failed callback attempt; archived login results are historical.

## Verification

All eleven active suites were freshly run: **320 passed, zero failed/skipped**. The final
runtime run passed all four cases in three minutes after the typed-result correction; the
affected HTTP suite was rerun after that correction as well.

| Suite | Passed |
| --- | ---: |
| Architecture | 49 |
| Actor identity core | 19 |
| Tenancy core | 17 |
| Context sample | 15 |
| EF model/write validation | 23 |
| Actor HTTP adapter | 15 |
| Independent tenancy HTTP adapter | 39 |
| Independent PostgreSQL ownership consumer | 6 |
| Persistence sample PostgreSQL | 36 |
| HTTP sample PostgreSQL/telemetry/setup | 97 |
| Aspire/Kestrel/PostgreSQL runtime and Chromium/OIDC | 4 |

The total comprises 177 container-free and 143 container/runtime cases. E3.7 adds six to
E3.6's 314. That checkpoint's results and archived login tests remain historical evidence;
they are not substitutes for the new provider/browser runs.

The final solution build succeeds for all 27 active projects with zero warnings/errors.
Native style/analyzer verification, CSharpier (198 files including generated migrations),
archive integrity (800 originals), local Markdown links, CI YAML/realm JSON parsing and
diff whitespace pass. The independent context console runs successfully. Technical library
and archive source changes were absent at the implementation handoff, with an empty index.
Checkpoint hooks additionally reran the 21 archived architecture cases successfully; those
are outside the active 320-case total.

Runtime tests used the separately installed pinned native CLI 13.5.4 through PATH and
rootless Podman. The matching Chromium binary was installed using Playwright's bundled
native installer; existing local system dependencies sufficed. CI adds the generated
PowerShell `install --with-deps chromium` step only to the runtime lane. GitHub-hosted CI
execution and that runner's dependency installation are not local results. Archived
application, PostgreSQL, broker and topology suites were not rerun in this slice.

## Library, template and sample findings

**No new reusable library mechanism was proven or extracted.** The existing independent
ActorIdentity, Tenancy, HTTP adapters and EF ownership utility work through actual OIDC and
browser ingress without acquiring authentication-provider or Access dependencies.

Template material gains optional native Keycloak/realm wiring, exact browser callback
registration, explicit external identity setup, native cookie/API challenge composition and
the executable browser proof/CI recipe. These editable sample sources are the current template
material; materialized template output and bootstrap CLI remain E10. Native ServiceDefaults
continues to supply useful defaults and needs no Foundry wrapper.

Consumer-owned policy includes provider selection/client configuration, application account
linking, registration, Organization membership/admission and failure responses. A single
provider does not justify a provider-neutral authentication abstraction. The previously
identified optional human-actor antiforgery requirement remains a later extraction candidate;
the current token binding and endpoint integration stay local.

## Review files and remaining gaps

Start with [AppHost Program](../../samples/Wholesale/AppHost/Program.cs) and
[realm configuration](../../samples/Wholesale/AppHost/Keycloak/wholesale-realm.json), then
[native authentication](../../samples/Wholesale/Api/NativeAuthentication.cs),
[typed profile read](../../samples/Wholesale/Api/Endpoints/CustomerProfileEndpoints.cs)
and [antiforgery endpoint](../../samples/Wholesale/Api/DemoComposition.cs),
[finite setup inputs](../../samples/Wholesale/Api/DemoIdentitySetup.cs),
[setup orchestration](../../samples/Wholesale/Api/DemoSetup.cs) and
[Access seed](../../samples/Wholesale/modules/Access/Access/AccessDemoSeed.cs).
Review [browser journeys](../../samples/Wholesale/RuntimeComposition.Tests/BrowserTests.cs),
[setup failure proofs](../../samples/Wholesale/IntegrationTests/Api/SetupInputTests.cs),
the removed HTTP fixture override, package pins, [CI](../../.github/workflows/ci.yml) and
the local instructions alongside those files.

The browser ignores development certificate trust errors; native API backchannel validation
stays enabled. API/provider are HTTPS localhost endpoints on different ports, hence the same
site. These results do not establish browser CA trust, external cross-site provider cookies,
production TLS, reverse-proxy/subdomain topology or provider compatibility beyond this pinned
local Keycloak. The preview hosting dependency remains an explicit sample limitation.

Logout/provider end-session, ticket stores/session invalidation, account lifecycle, explicit
linking, membership administration, roles/permissions, open registration and tenant-aware
native authorization handlers remain separate capabilities. This closes the bounded E3
state-stored ingress proof, not those additional features. The next
planned slice is [E4 durable event identity/payload codecs](../plans/e4-event-serialization.md), exercised by two event families
without requiring messaging or EF.
