# E3.6 Runtime composition and native telemetry

Status: owner-reviewed and checkpointed as `28ee797` after E3.4/E3.5 checkpoint `31c7a8b`.
All configured pre-commit and commit-message hooks passed. See [the scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-6-runtime-composition.md)
and [runtime guide](../../samples/Wholesale/AppHost/README.md).

## Outcome and control

The active solution has 27 projects, including a populated sample AppHost, editable native
ServiceDefaults and a runtime suite. Native Aspire 13.5.4 composes PostgreSQL 18.6, its
database, the existing API and an explicitly started finite setup command. Project-path
references avoid generated application handler/types. Root `aspire.config.json` selects
the active AppHost; archived sources and fixtures remain unchanged.

API startup waits for the database resource but never applies module migrations or seeds.
`demo-setup` executes the existing `--initialize-demo` path only when explicitly started.
The test waits for its exit before asserting healthy readiness/data; readiness itself only
checks tables and may become healthy during seeding. Setup is not an API startup dependency or repeatable
reconciliation. Native database creation does not imply application schema creation.

The host explicitly registers and maps readiness/liveness. `/health/live` remains healthy
during a database failure; `/health/ready` checks connectivity and seven required tables,
with a native five-second health-check timeout. Both permit anonymous/tenantless requests.
`/health` retains its existing liveness response. Readiness performs no tenant lookup,
write, migration or repair and does not certify full migration history, seeded rows or OIDC.

Sample ServiceDefaults retains console logging and registers native OpenTelemetry request,
HTTP, runtime and Npgsql producers. OTLP export is conditional on the native endpoint setting.
No global HTTP retry/resilience handler, persistence middleware, transaction automation or
custom telemetry parser is added. Finite setup returns before host/exporter registration.

Local PostgreSQL defaults to a retained named volume. Tests and the manual disposable proof
choose `LocalDevelopment:UseDataVolume=false`; the container is session-owned. A stable
password is required for retained storage. No existing personal graph or volume was removed.

## New evidence

The native Aspire.Hosting.Testing case starts real Kestrel/PostgreSQL with randomized ports
and ephemeral data. It verifies that startup leaves representative module tables absent and
setup inactive, with liveness 200/readiness 503. A native start command runs setup to exit 0;
readiness becomes 200 and independently expected catalog values are 42/7. Native PostgreSQL
stop produces readiness 503, liveness 200 and a failed catalog read. This is an executable
startup/setup/failure protocol proof, rather than a snapshot of resource annotations.

Two focused HTTP/PostgreSQL cases exercise actual ServiceDefaults through native in-memory
exporters. They verify a real catalog request's server Activity and direct child Npgsql
Activities share trace identity, and request-duration metrics reach the provider. Renaming
the Inventory table deliberately makes the same operation fail: the request/database have
error status and native error logs carry the same trace identity. These tests protect our
producer/provider wiring and failure observability, not OpenTelemetry's internal encoding.

A separate manual `aspire start --isolated --no-build` run used ephemeral storage and inert
OIDC configuration. Native CLI waits confirmed the database running and the API initially
unready. Explicit setup made the API healthy and catalog reads returned 42/7. Actual dashboard
OTLP data exposed a successful request with child Access and Inventory spans sharing its
trace ID and parent span relationship. Stopping PostgreSQL yielded 503/200/500 for readiness,
liveness and catalog. `aspire otel traces` exposed the failed server span and failed Npgsql
connect span; `aspire otel logs --severity Error` returned matching error logs. Native
`aspire stop` stopped the task-owned graph. This manual observation is separate from the
automated exporter/runtime cases; no binary substring or custom collector was used.

CLI 13.5.4 exposes logs/spans/traces, not a metrics command. Automated native exporter cases
prove metrics provider collection; manual dashboard metric rendering/export was not asserted.
OTLP delivery is asynchronous, so an immediate CLI query can precede exported data.

## Verification

Final active solution build: 27 projects, zero warnings/errors. Native style and analyzers
pass. The pinned native CLI installation command was independently executed in a temporary
tool directory; the final runtime case uses that CLI through its PATH. CI has a separate
active runtime lane using the same pinned package, native dev certificate, solution build
and runtime case. GitHub-hosted execution itself is not a local result.

All eleven active suites were freshly rerun: **314 passed, zero failed/skipped**.

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
| HTTP sample PostgreSQL/telemetry | 94 |
| Aspire/Kestrel/PostgreSQL runtime | 1 |

The total comprises 177 container-free and 137 container/runtime cases. E3.6 adds three
cases to the previous 311: two native telemetry cases and one coherent runtime lifecycle
case. CSharpier (194 files), archive integrity (800 originals), local Markdown links
(484 links in 51 active documents) and diff whitespace pass. The independent context console
also runs successfully. Archived application/broker/topology suites were not rerun here.

## Extraction and template findings

**No new reusable library mechanism was proven or extracted.** The five existing technical
segments remain unchanged and independently adopted. There is no new dependency from a
library to Aspire, OpenTelemetry export, Access or sample module Contracts.
The owner confirmed native ServiceDefaults is useful template material and warrants no
Foundry library wrapper.

The exercised template recipe now includes the AppHost graph, explicit setup resource,
editable ServiceDefaults, health endpoint policy, required parameters and native lifecycle
commands. This source is the template material; no separate generated-template tree or
bootstrap CLI is added before E10. Consumer-owned choices include OIDC provisioning/client
policy, volume retention, readiness tables, exporter destinations and module registration.

The sample proves that the already-extracted identity/tenancy/EF utilities work in a native
orchestrated application without a new runtime coordinator. This does not establish new
membership/domain reuse. The owner narrowed the previous antiforgery candidate to an
optional human-actor requirement; the current HTTP/native token integration remains local.

## Review files and remaining gaps

Start with [AppHost Program](../../samples/Wholesale/AppHost/Program.cs),
[ServiceDefaults](../../samples/Wholesale/ServiceDefaults/Extensions.cs),
[readiness check](../../samples/Wholesale/Api/WholesaleDatabaseHealthCheck.cs)
and [health mapping](../../samples/Wholesale/Api/RuntimeHealthEndpoints.cs).
Then review [runtime usage/proof](../../samples/Wholesale/RuntimeComposition.Tests/RuntimeTests.cs),
[telemetry proofs](../../samples/Wholesale/IntegrationTests/Api/TelemetryTests.cs), native
package/solution changes, [CI](../../.github/workflows/ci.yml) and the runtime instructions.

E3.6 public requests use inert OIDC settings and no authentication substitute; they never
contact a remote provider. Native HTTP/cookie proofs remain useful but cannot certify real
OIDC login/callback/session or Secure-cookie browser behavior. HTTPS endpoint startup was
observed; browser certificate trust/callback configuration was not proven. E3.7 supplies
the disposable provider and actual browser mutation journey before returning to E4 codecs.
Membership administration, account linking, shared cross-module transactions, deployment,
exporter-outage delivery/shutdown guarantees, reservation and event-sourced stock remain
separate capabilities. Existing E2/E3 tests are freshly rerun where listed; archived topology
and broker results remain historical evidence.
