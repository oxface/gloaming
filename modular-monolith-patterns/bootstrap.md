# Initialize or adapt an application

Read [structure](structure.md) first. Use native .NET/Aspire tools and the target repository's
existing package/tool manifests. This task establishes runnable composition and the first
requested behavior; it does not copy Gloaming sources into the application.

## 1. Establish tooling and package consumption

Choose a solution/application name and compatible .NET target, then pin the SDK and central
package versions. Match existing nullable, analyzer and test-runner conventions. Add ordinary
build/test commands, formatting and CI appropriate to the selected projects.

Consume selected Gloaming packages through ordinary PackageReferences.
[Library integration](gloaming.md) describes the local-feed workflow and capability selection.
Inspect package framework/API compatibility before copying a snippet from another version.

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

Use separate named connection options per module and per restricted role. Runtime startup
registers services; it does not silently migrate, seed or grant database privileges.
Development example data is a separately selected finite operation.

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

Read the selected [HTTP/context contracts](gloaming.md) for ordering. Authentication and effective
policy evaluation precede actor completion; tenant admission is completed before protected
module work. The module owns late resource checks. Configure native authorization and
antiforgery for the chosen HTTP interaction.

If the actual issuer or account/membership model is undecided, expose the configuration seam
and record the missing decision. Protected business operations require a real admitted context;
a placeholder identity must not silently authorize production work.

Done: tests prove authorized/unauthorized paths and context establishment. When an actual local
OIDC provider is configured, prove login/callback and one protected operation with that provider.

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
