# Verify the resulting application

Apply the target repository's checks, then verify the selected behavior. Record actual commands
and outcomes; a project list or copied test does not establish working composition.

## Build and architecture

- Restore/build the selected solution with its pinned SDK/packages.
- Verify module implementations do not reference each other or host projects.
- Verify business callers use Contracts; composition and migration roots may reference
  native implementation configuration deliberately.
- Ensure library dependencies match selected capabilities and local package versions restore
  without Gloaming source ProjectReferences.
- Verify all solution, tooling and CI paths against the actual target repository.

## Module persistence

For each requested module, exercise a real accepted operation and read it through its API.
Cover invalid/stale work, native rollback and module/tenant isolation. A new module's schema,
migrations and migration history belong to it.

Use the actual provider for provider guarantees. If data already exists, prove a populated
forward migration and compatible payload/result readers; a new empty database is insufficient.
For selected event sourcing, include event/head/inline-state atomicity and optimistic conflicts.

For RLS, verify restricted runtime credentials, policies, transaction-local context, denied
foreign writes and unfiltered SQL behavior. Query filters alone cannot prove these properties.

## Hosts and operations

Verify Server startup and one real endpoint. Verify Migrator applies the selected migrations
and exits with failure when setup fails. Verify AppHost resource references, migration completion
ordering and configured readiness before claiming local orchestration works.

For configured OIDC, use the actual local issuer for one login/callback/protected flow.
Test-only authentication verifies application policy separately and does not certify OIDC.
A selected worker must establish fresh admitted scopes, propagate cancellation, expose faults
and follow the chosen native supervision/restart policy.

## Durable behavior

When messaging is selected, prove transactional enqueue/intake, duplicate handling, rollback,
competing consumers, uncertain completion and actual process recovery. External provider effects
need their own idempotency/reconciliation policy.

When command receipts are selected, prove replay of the original result after uncertain commit,
input conflict and current authorization. Do not repeat a business decision because decoding an
old retained result fails.

## Handoff

Update the consuming application's architecture and setup docs with its module map, native
infrastructure, credentials/configuration names and transaction owners.
Report implemented behavior, checks actually run, configuration still required and unresolved
business decisions. Keep unsupported guarantees explicit. Avoid empty placeholders and invented
extra modules merely to fill the preferred layout.
