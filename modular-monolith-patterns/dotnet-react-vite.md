# .NET, React and Vite bootstrap profile

Apply this profile when the application selects native ASP.NET Core cookie/OIDC authentication,
PostgreSQL, a React/Vite frontend and Aspire orchestration. Follow [bootstrap](bootstrap.md)
and the [first-module walkthrough](first-module.md) for backend ownership. Existing repository
instructions and explicit owner choices take precedence. These are consumer implementation
defaults; Gloaming supplies no frontend package, generated template or required runtime.

## Pinned workspace and tooling

Use `apps/<portal>` for application source and private pnpm workspace packages under `packages/`.
Pin Node, pnpm, the .NET SDK, central NuGet versions and local .NET tools. Commit workspace,
orchestration npm and NuGet dependency locks. Include the AppHosts in locked solution restore.

Supply working editor configuration, manifests, root commands and matching CI checks together:

| Concern         | Starting behavior                                                                                                                                                                                                             |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Formatting      | Prettier for frontend/configuration; CSharpier for handwritten C#/project XML. Declare generated API types, EF migrations, lock files and build output policies explicitly.                                                   |
| C# spacing      | Enable IDE2003 through `.editorconfig` and Roslyn style verification. CSharpier preserves logical groups but does not infer them. Agent guidance asks for semantic grouping and keeps `else`, `catch` and `finally` attached. |
| Type-aware lint | Pin Oxlint and `oxlint-tsgolint`; enable `typescript/no-deprecated` and fail warnings. Cover application and shared workspace source. Use the installed React declarations' supported event types.                            |
| Git hooks       | Lefthook runs checks against staged blobs and conventional commit validation. Hooks preserve the user's index and working tree.                                                                                               |
| Install scripts | Use the policy supported by the pinned pnpm version. For pnpm 12, configure explicit `allowBuilds` entries for required packages; verify them during clean installation.                                                      |

Keep this commitlint configuration, including its disabled capitalization check:

```js
export default {
  extends: ["@commitlint/config-conventional"],
  rules: {
    // Capitalization does not change the meaning of a conventional commit.
    "subject-case": [0],
  },
};
```

Accept a capitalized Dependabot subject such as `chore(deps): Bump dependency`; reject malformed
messages through the same configuration in hooks and CI. Pin tool versions in manifests rather
than copying a dated dependency list from this guide. See [type-aware lint setup](https://oxc.rs/docs/guide/usage/linter/type-aware.html)
and [pnpm build settings](https://pnpm.io/settings#allowbuilds).

A staged-content helper exports the complete index into scratch storage, uses staged tool
configuration, and redirects workspace dependency symlinks to exported package source. External
dependencies may reuse installed packages matching the staged manifests/locks; require an install
when they differ. Checking working-tree files by staged filename does not check the commit.
CSharpier accepts explicitly named files even when normally ignored, so apply the staged ignore
policy before passing its file list. Retain isolated Git fixtures for both staged/unstaged
directions, including a workspace dependency and generated migrations.

Use `eng/` for enduring verification/codegen helpers, preferably TypeScript for new durable
scripts. Document their runtime and invocation. Inventory temporary package bridges separately
with a removal condition; remove superseded one-off helpers. Keep workspace source visible to
Git/formatters/lint while excluding every nested `node_modules`, build output and generated cache.

Done: a frozen clean install and the scaffold's own formatting, type-aware lint and hook fixtures
pass without changing source or the index.

## Solution-owned orchestration

Create `orchestration/local/<Application>.Local.AppHost.csproj` using `Aspire.AppHost.Sdk`,
with a C# entrypoint, `Aspire.Hosting.AppHost` and explicit runnable project references. Include
it in the main `.slnx`. Pin the SDK/package versions and use standard
`Properties/launchSettings.json` for dashboard/telemetry ports and environment settings.
The [Aspire SDK](https://aspire.dev/get-started/aspire-sdk/) describes typed `Projects.*`
metadata and CLI-bundle configuration.

When an isolated browser suite exists, add a sibling E2E AppHost and a shared orchestration
class library. Reference the latter with `IsAspireProjectResource="false"`; runnable backend
references remain in each AppHost. Keep entrypoints thin. Share explicit infrastructure,
backend/portal and identity wiring; use named local/E2E profiles for ports, database names and
persistence. Additional portals get application-specific resource names and wiring. Keep a
future publishing AppHost absent until its deployment behavior is implemented.

Use an independent npm-locked Aspire CLI under `orchestration/local`, even though application
packages use pnpm. Document `npm ci --prefix orchestration/local` separately. Install workspace
dependencies once, then use `.WithPnpm(install: false)` for Vite resources. A root
`aspire.config.json` selects the default local project; commands/tests for other hosts always
pass their exact project path. Shared editor launch entries select hosts while project launch
profiles retain runtime settings. Reconcile editor-extension and command-line tool versions.

With Aspire CLI 13.6.4, the observed central `Aspire.Hosting.AppHost` version form is `13.6.4`:
`[13.6.4]` builds but fails CLI startup version parsing. Keep the dependency lock and verify
actual CLI startup for the selected toolchain; this is not a general change to NuGet pinning.

Persist local PostgreSQL, identity-provider data, key storage and generated secret parameters.
Keep issuer/callback endpoints stable and explicitly migrate persisted identity configuration;
realm import does not itself reconcile an existing realm. Preserve volume names and UserSecrets
IDs during host/project moves. Allocate application-specific names for a new scaffold rather
than copying a consumer's migration-specific volume names. Include pgAdmin locally, discovered
through the dashboard; disposable E2E stacks omit it.

Done: solution restore/build covers orchestration, the pinned CLI starts the selected host,
the finite Migrator gates backend readiness, and a restart preserves data and identity.

## Browser authentication and serving

Let Vite serve the SPA in development. Proxy `/api`, `/auth`, `/signin-oidc`,
`/signout-callback-oidc` and `/signout-oidc` to the backend HTTPS endpoint. Preserve the public
frontend Host (`changeOrigin: false`) so native OIDC constructs the intended callbacks. A local
development-certificate exception belongs only to that development proxy. The backend serves
API/auth endpoints; remove SPA static-file middleware and `MapFallbackToFile("index.html")`
for this topology. Production serving, ingress and forwarded-header trust require their own
configuration and checks.

Keep browser API calls same-origin with native secure, HttpOnly cookies. Return API 401/403
responses without browser-login redirects; navigate to login deliberately. Protect mutations
and the logout POST with native antiforgery. Put generic HTTP filters in host HTTP infrastructure
and use native local-return URL validation. Application modules keep business authorization.

Use authorization-code OIDC with PKCE. When the selected provider needs an ID token for
RP-initiated logout, retain only that token in the encrypted authentication ticket. Inside
the native `AddOpenIdConnect` options, compose this with any existing validated-token event:

```csharp
options.ResponseType = "code";
options.UsePkce = true;
options.SaveTokens = false;
options.Events.OnTokenValidated = context =>
{
    var idToken = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken;
    if (!string.IsNullOrEmpty(idToken))
    {
        context.Properties!.StoreTokens([
            new AuthenticationToken { Name = "id_token", Value = idToken },
        ]);
    }

    return Task.CompletedTask;
};
```

Map logout to both schemes, using the same `"oidc"` name supplied to `AddOpenIdConnect`:

```csharp
app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(context);

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, "oidc"]);
    })
    .RequireAuthorization()
    .AllowTenantless()
    .ExcludeFromDescription();
```

These excerpts use `Microsoft.AspNetCore.Authentication`, its `.Cookies` namespace,
`Microsoft.AspNetCore.Antiforgery` and `Gloaming.Tenancy.AspNetCore`, with native authentication
and antiforgery already registered. The browser submits the native antiforgery form token or
configured header. Configure the provider's callback/logout registrations and map validation
failures to the application's 400 problem response. Access and refresh tokens remain unstored;
keeping identity claims alone does not supply the provider's logout token hint. Follow the
[native OIDC composition guide](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication).

Persist native Data Protection keys in Azurite locally and provision its container before the
backend starts. Replicas of one application/environment share the intended keyring/application
discriminator; independently trusted hosts and environments have separate rings. Configure
production storage access and key wrapping separately. Ephemeral keys are an explicit isolated
test or schema-generation choice. Schema generation must not relax normal runtime configuration.

Done: an actual browser completes login/callback, a protected operation and provider logout
through the frontend origin; local restart preserves the signed-in cookie.

## Generated HTTP contracts

Use native .NET OpenAPI generation with `Microsoft.AspNetCore.OpenApi` and build-time
`Microsoft.Extensions.ApiDescription.Server`. Give JSON operations stable names, explicit
route parameters, success types and problem responses. A tenant parameter consumed only by
middleware must still appear in endpoint/OpenAPI metadata. Exclude browser redirect endpoints
from the JSON client contract.

Build-time schema generation executes the entrypoint with a mock server. Follow the native
[entry-assembly detection guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0#customize-runtime-behavior-during-build-time-document-generation)
for `GetDocument.Insider` to bypass infrastructure startup/runtime secrets only during that
generation. Keep endpoint/service descriptions available. Normal startup still requires its
real database, identity and key configuration.

Generate reviewed frontend types with `openapi-typescript` and consume them through
`openapi-fetch`. Keep application-owned TanStack Query caching and mutation boundaries. Define
root `generate:api` and non-writing `generate:api:check` commands; CI builds the backend schema,
checks generated-client drift, then type-checks and builds the frontend. Handwritten API
wrappers may compose generated types; DTO copies are not an additional contract source.

Done: generation succeeds without live PostgreSQL, cloud services or runtime secrets, and the
generated client compiles with every selected route parameter and response shape.

## Frontend ownership and state

Keep `main.tsx` to composition, with individual views and focused session/data hooks. A private
workspace app-shell package owns presentation slots/styles; application routing, account state
and business behavior stay in the portal. A separate private UI source package owns shared
controls and Tailwind theme tokens. Use shadcn's Base UI registry, pinned generation tooling
and matching application/package `components.json` aliases following its
[monorepo guide](https://ui.shadcn.com/docs/monorepo). Include shared component source in
Tailwind scanning and consume the controls in real views. Keep generator provenance/licenses.

Enable stable React Compiler transforms in development and production for the installed React
major, including linked workspace source. For `@vitejs/plugin-react` 6+, use
`reactCompilerPreset` through `@rolldown/plugin-babel`; follow the
[version-matched installation guide](https://react.dev/learn/react-compiler/installation).
Inspect representative optimizable application, shell and shared-control output; a successful
Vite build alone does not establish that the compiler transformed linked source.

Include account identity and tenant/resource identity in scoped query keys. Reset view and
form state when the resource changes; version alone is insufficient because different resources
can have equal versions. Include identity plus version when resetting an editor to refreshed
server state, preserving the application's intended unsaved-edit behavior. Mutation invalidation
stays application-owned. Treat 403 membership revocation and 409 stale writes explicitly.

Done: cached navigation between two resources with equal versions displays the correct data
and form values. Switching accounts cannot display another account's cached data; a suspended
membership loses protected access while any remaining account/organization access follows policy.

## Clean-consumer acceptance

Provide these root commands with the behavior above. Run against a clean checkout and record
the exact toolchain, Gloaming revision/version/archive source and outcomes:

1. Prepare the selected package source as described in [library integration](gloaming.md).
   Run `pnpm install --frozen-lockfile`, `npm ci --prefix orchestration/local` and
   `dotnet tool restore`, then locked solution restore from an initially empty NuGet package cache.
2. Run `pnpm format:check` and `pnpm lint`, plus the retained staged-content fixtures. Check
   both the capitalized conventional-commit example and an invalid message through commitlint.
3. Build the backend/native OpenAPI, run `pnpm generate:api:check`, then `pnpm check:web`,
   `pnpm check:e2e` and `pnpm build:web`. Include workspace UI/shell in type/lint checks.
   Verify development and production compiler output for representative shared source.
4. Run native PostgreSQL integration/architecture checks through the finite Migrator and
   restricted runtime role. Include admission/direct Contracts rejection, registration races,
   stale writes, audit rollback and forbidden schema/audit SQL from [verification](verification.md).
5. Install Playwright Test's browser and Linux system dependencies where required. Run
   `pnpm test:e2e` against a sibling E2E AppHost with its own disposable PostgreSQL, identity
   realm, key storage, secret parameters and ports. Cover actual OIDC/logout, CSRF, generated
   client operations, account changes, equal-version resource navigation and membership revocation.
6. Hold an exclusive E2E run lock from startup through teardown, refuse occupied ports and
   target the exact E2E AppHost. Retain ownership/recovery information after failed stop;
   verify the developer stack/data survive. Separately prove developer restart persistence.

Use Playwright Test for browser journeys and xUnit with Microsoft Testing Platform for new
.NET integration/architecture checks, preserving an existing application's selected runner.
Retain failure traces/screenshots and cleanup instructions.
Automate ongoing acceptance checks in consumer CI; label one-off or historical execution and
remote CI/deployment gaps in the handoff. A local browser result does not establish production
ingress, customer federation or another Gloaming capability's support.
