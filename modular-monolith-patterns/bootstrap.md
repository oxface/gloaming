# Initialize or adapt an application

Read [structure](structure.md) first. Use native .NET/Aspire tools and the target repository's
existing package/tool manifests. This task establishes runnable composition and the first
requested behavior; it does not copy Gloaming sources into the application.

## 1. Establish tooling and package consumption

Choose a solution/application name and compatible .NET target, then pin the SDK and central
package versions. Match existing nullable, analyzer and test-runner conventions. Add ordinary
build/test commands, formatting and CI appropriate to the selected projects.
Supply pinned tool manifests, editor configuration, generated-file policies and root commands
together. The initial handwritten scaffold must pass its own formatting and analyzer checks.
Hooks check reviewed staged content without formatting or re-staging the index. The
[browser stack profile](dotnet-react-vite.md) supplies the selected frontend/tooling defaults.

Consume selected Gloaming packages through ordinary PackageReferences.
[Library integration](gloaming.md) describes the local-feed workflow and capability selection.
Inspect package framework/API compatibility before copying a snippet from another version.
Account for the [distribution boundary](gloaming.md#distribution-boundary): a local snapshot
must remain available to a clean consumer, and rebuilding its source need not reproduce its bytes.

Done: restore/build uses the intended SDK and selected package versions, without source
ProjectReferences back into the Gloaming checkout.

## 2. Compose native hosts

Create or adapt:

- Server: native ASP.NET Core entry point, endpoints, authentication/authorization, module
  registration and selected operational health/telemetry.
- Migrator: finite executable that applies the requested module migrations/provisioning,
  reports failure and exits. It uses setup credentials and does not serve application traffic.
- Worker: native host for explicitly selected background roles. Add work only for an actual
  requirement; name its roles and establish a fresh operation scope for each turn.
- AppHost: local resource graph and references for the application, database, optional identity
  provider, migrator and selected workers.
- ServiceDefaults: consumer-owned shared native telemetry/health setup when useful to these hosts.

Prefer C# SDK-style AppHost projects under `orchestration/local`, included in the main solution
for project-aware editing and solution restore/build. Keep existing working orchestration when
adapting another stack. Use explicit runnable project references; mark shared orchestration
library references with `IsAspireProjectResource="false"`. Create sibling E2E or publishing hosts
when their behavior is needed, sharing application wiring while keeping lifecycle/data ownership
separate. Preserve secret identities, volume names and public endpoints during host conversions.

Use separate named connection options per module and per restricted role. Runtime startup
registers services; it does not silently migrate, seed or grant database privileges.
Development example data is a separately selected finite operation.
The finite Migrator invokes explicit module setup entry points; each module owns its migrations
and schema-specific grants. Hosts own infrastructure provisioning. Readiness probes use actual
runtime connections/credentials; migration completion and tests separately prove schema setup.

Done: each selected executable builds and has one clear role. Startup failures are visible.
Database-dependent hosts wait for required resources and successful migration completion in
local orchestration.

## 3. Wire identity and tenant admission

Use native ASP.NET Core authentication, including cookie/OIDC when a browser login is requested.
Authority, client IDs, redirect/logout URIs, trust and secrets are application configuration.
Store secrets through the target's chosen secret mechanism.

Map authenticated identity explicitly into application identity/actor context.
Choose tenantless or selected-tenant execution deliberately. Tenant membership/admission and
resource permissions belong to the application; a tenant key is not permission.
Treat the route/header tenant as a candidate until admission succeeds. Gloaming tenant keys
are opaque; GUID requirements, slug lookup and membership rules are application policy.

When first login creates an application user, key external identity by the validated issuer
and subject, with a native unique constraint. Concurrent registration must recover the same
account from fresh state after a conflicting transaction. When registration is audited, stage
that audit in the same transaction so only the accepted registration's audit remains.
Establish request account state once and guard reads until establishment is complete. Keep this
pre-admission registry separate from tenant-owned business persistence.

Read the selected [HTTP/context contracts](gloaming.md) for ordering. Authentication and effective
policy evaluation precede actor completion; tenant admission is completed before protected
module work. The module owns late resource checks. Configure native authorization and
antiforgery for the chosen HTTP interaction.
Module operations check current authority for direct Contracts callers as well as HTTP callers.
Membership suspension can revoke one organization's access while preserving the signed-in
global account and other memberships; use the application's actual revocation policy.

For browser cookies, configure persistent native Data Protection and prove a restart preserves
the session. Replicas of one application/environment share the intended keyring and discriminator;
independently trusted hosts/environments have separate rings. The
[browser security recipe](dotnet-react-vite.md#browser-authentication-and-serving) covers local
storage, OIDC logout and proxy composition; production storage/access/key protection stay explicit.

If the actual issuer or account/membership model is undecided, expose the configuration seam
and record the missing decision. Protected business operations require a real admitted context;
a placeholder identity must not silently authorize production work.

Done: tests prove authorized/unauthorized paths and context establishment. When an actual local
OIDC provider is configured, prove login/callback, one protected operation and logout with that
provider through the chosen frontend origin.

## 4. Add the requested modules

Follow [modules](modules.md). Register every module explicitly in Server and the appropriate
Worker/Migrator. The host supplies native options; a module does not discover other modules,
choose global transport or configure the application's authentication provider.

The [first-module walkthrough](first-module.md) connects these steps for one protected
state-stored operation with transactional audit.

Wire the first real endpoint/operation for each requested module. Keep sample aggregate behavior
minimal when the request supplies only names.

Done: the first requested create/load path reaches native module persistence through Contracts.

## 5. Complete application verification

Follow [verification](verification.md). Exercise migration failure/startup ordering, native
rollback and required tenant isolation. Use real infrastructure for guarantees that depend on it.
Document exact local setup and selected production integration obligations.
