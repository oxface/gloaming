# A2.7: optional Aspire Sales RLS provisioning

Implemented for owner review on 2026-10-17. The owner approved committing the A2.6 snapshot
and continuing on the same branch. A2.6 was committed as `521e9b3` with all repository hooks
enabled/passing; this next increment remains unstaged. The [scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a2-7-aspire-sales-rls.md)
and [operator guide](../../samples/Wholesale/AppHost/README.md#optional-sales-rls-graph) describe
the bounded local composition. There is no new Gloaming interface or worker adoption.

## Outcome and startup ownership

`LocalDevelopment:UseSalesRowSecurity=true` supplies password-free runtime connection references
plus two secret password parameters. Native PostgreSQL and optional Keycloak resources remain.
Both `demo-setup` and `api` require explicit start in this mode:

1. Start the graph/database. No runtime roles, module schemas, RLS policies or seed rows are installed.
2. On fresh storage, start `demo-setup`, wait for it to finish and verify exit 0.
3. Explicitly start `api`; it checks actual runtime roles/policy inventory before accepting work.

On initialized retained storage, start the API without rerunning setup. This avoids making
one-shot seed work an automatic dependency of every AppHost session. It is not an upgrade,
backfill, reconciliation or credential-rotation protocol. The default graph remains unchanged
and claims no RLS. All HTTP transaction/operation boundaries and module permission predicates
are those already proven in A2.6; no extra request scope or middleware transaction is added.

The finite `--initialize-rls-demo` mode alone receives `ConnectionStrings:Setup`. It validates
identity/configuration, creates the two named runtime logins in one native transaction, runs
existing explicit migrations/seed, grants Access/catalog rights, then installs the six Sales
policies/grants and checks both runtime credentials. PostgreSQL `format` quotes role identifiers
and password literals with `%I`/`%L`, as specified by its
[format reference](https://www.postgresql.org/docs/18/functions-string.html#FUNCTIONS-STRING-FORMAT).
Existing roles are never altered. Later setup stages have independent
existing commit boundaries; failure can leave partial initialization and is not auto-repaired.

## Proven boundaries and limits

| Boundary | Composition | Limitation |
| --- | --- | --- |
| Provisioning authority | Only the explicit setup resource references the admin connection. | Local disposable-demo role creation uses cluster authority; production provisioning is separate. |
| Sales runtime | `wholesale_sales`, non-owner/non-superuser/non-BYPASSRLS; existing six-table policies/grants. | Tenant equality, not within-tenant permission or credential-holder/GUC-forgery protection. |
| Access/Inventory runtime | `wholesale_access`, no Sales schema USAGE/table authority. | Access admission/catalog policy remains consumer-owned and is not RLS-covered here. |
| Secrets and connection strings | Separate secret parameters; sample calls Npgsql's builder once to combine them. | Other administrative secrets must also stay out of API configuration. No general secret sandbox is claimed. |
| Inherited environment | Three known setup/migration/admin keys explicitly receive empty overrides; normal RLS HTTP rejects nonempty provisioning values. | Arbitrary unrecognized credentials/environment remain deployment-owned. |
| Readiness | Same seven-table presence/connectivity check through pg_class/pg_namespace metadata. | No Sales schema grant, business-row query, policy re-attestation or automatic repair. |
| API / finite setup ordering | Both require explicit start in selected mode. | Starting API too early fails; readiness is not a setup-completion signal. Verify setup exit 0. |
| Retained storage | API can start without rerunning one-shot setup. | Keep passwords stable; rotation, upgrade and full restart/restore certification are separate. |
| HTTP work | Same immutable admission, caller transactions, automatic local tenant setup and profile/audit atomicity. | Worker transport bootstrap, other Sales capabilities and default RLS selection remain separate. |

Two integration findings grounded the extra consumer code. Native null environment overrides
did not remove an inherited admin parameter; the API startup guard rejected that attempt.
Explicit empty overrides then allowed startup with all three forbidden keys deliberately
present in the parent CLI environment. Also, the old readiness `to_regclass` checks required
Sales schema USAGE. The restricted Access role correctly failed that check. Readiness now
uses catalog metadata, retaining the narrower role rather than granting it Sales authority.
The query declares the required tables once and checks that none are missing; it has no
separate hard-coded table count to keep in sync with that list.

The password-normalization helper is consumer configuration code, not another tenancy/RLS
interface. Native [Aspire secret parameters and reference expressions](https://aspire.dev/fundamentals/external-parameters/)
supply its inputs. It prevents secret text being interpolated into a connection string and is exercised
with quotes, semicolons and backslashes. The native runtime role/policy verifier remains the
reviewed A2.6 component. No provider package versions, core dependencies or library code changed.

## Verification

The isolated Aspire CLI proof has passed: baseline startup/inspection, final selected graph,
explicit setup exit 0 and healthy API. The final parent environment deliberately included all
three forbidden provisioning keys. Disposable storage was selected and the graph was stopped;
no personal volumes were deleted. API startup initially rejected inherited credentials and the
readiness schema permission issue; the corrections above were verified in the final proof.

Fresh checks from this checkout:

| Suite / check | Result |
| --- | --- |
| Complete native Aspire/runtime/browser suite | **6 passed**, zero skipped: four browser cases plus ordinary and RLS graph cases. Final full run passed in 4m09s. |
| HTTP/PostgreSQL composition | **164 passed**, zero skipped, including three new provisioning-credential rejection cases before business connections open. |
| Architecture | **96 passed**, zero skipped; boundary rules unchanged. |
| Full solution build | Passed with zero warnings/errors using `--no-restore`; no fresh audited restore or dependency security attestation. The prior advisory/workaround state recorded in A2.6 is unchanged; no dependency/audit configuration changes. |
| Full active style / analyzers | Both passed without modifying files. |
| CSharpier / frozen archive | 797 files checked; all 800 original archived files preserved. |
| Documentation / whitespace | 426 local links/anchors across seven changed Markdown documents resolved; changed-file whitespace checks passed. |

**266 native tests passed**, without double-counting retries. Runtime proofs use PostgreSQL
18.6, Keycloak 26.8.0, native Aspire/Kestrel and actual Chromium/OIDC login. The RLS browser
case verifies both runtime logins and six ENABLE/FORCE policies before login, then verifies
the accepted audit, unchanged Beta state and a Sales-login backend after the protected edit.
The independent graph case proves no application setup on graph start, explicit setup exit 0,
restricted-role catalog checks, Access readiness without Sales schema USAGE, missing-context
database denial and a WITH CHECK rejection of a foreign owner update. Runtime passwords with
quotes/semicolons/backslashes survive actual provisioning and connections.

The browser fixture initially assigned its mode after graph creation. Its final database
assertion rejected that false composition; the selector now goes into startup arguments and
the explicit role/policy assertion prevents an ordinary graph being counted as an RLS proof.
One ordinary Alpha browser case later hit a native HTTPS readiness timeout. It passed in
isolation in 41s, then the complete six-case suite passed with no concurrent build/verification
process. No test timeout, TLS bypass or architecture assertion was weakened to obtain a pass.

After removing the duplicated readiness table count during owner review, the existing
restricted-runtime Aspire graph proof was rebuilt and rerun: **1 passed**, zero skipped
(28s). That proof requires healthy API readiness while Access still has no Sales schema
USAGE. The changed C# file also passed CSharpier and whitespace checks. The complete-suite
results above precede this equivalent-query cleanup; they were not rerun for it.

Prior A2.6's 373 tests and earlier AppHost/browser results remain historical evidence and are
not counted as new A2.7 results. No new template, production deployment, retained-volume
upgrade/restore, external pooler or provider-failover proof is claimed. All 16 new/changed
files remain unstaged; the post-commit index and staged diff match their pre-edit snapshots.

## Extraction and review

**Library:** no new reusable mechanism was proven or introduced. A2.5/A2.6 supply the existing
isolation mechanism; core Tenancy still has no mandatory Access/FGA/provider dependency.
**Sample:** native orchestration, explicit setup, credentials/grants, password assembly and
readiness remain consumer-owned. **Template:** no generated files/presets changed.

Review-worthy files:

- [AppHost graph](../../samples/Wholesale/AppHost/Program.cs) and
  [operator workflow](../../samples/Wholesale/AppHost/README.md#optional-sales-rls-graph).
- [Finite setup](../../samples/Wholesale/Api/RlsDemoSetup.cs),
  [password assembly](../../samples/Wholesale/Api/DemoDatabasePasswords.cs),
  [program/credential guard](../../samples/Wholesale/Api/Program.cs) and
  [catalog readiness](../../samples/Wholesale/Api/WholesaleDatabaseHealthCheck.cs).
- [Native graph proof](../../samples/Wholesale/RuntimeComposition.Tests/RowSecurityRuntimeTests.cs),
  [browser journey](../../samples/Wholesale/RuntimeComposition.Tests/BrowserTests.cs) and
  [startup rejection cases](../../samples/Wholesale/IntegrationTests/Api/SetupInputTests.cs).

The remaining work is worker/transport role composition, default rollout if selected,
production provisioning/upgrade/rotation, automatic policy scaffolding, creation/submission/
grant administration, template materialization and real external pooler/failover proofs.
CLI/native local startup is not deployment or restore certification. All new changes remain
unstaged, preserving the post-commit index; no next commit or push is authorized by this report.
