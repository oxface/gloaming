# T2: Catalog decisions, native queries and an optional report worker

Date: 2026-10-22. Bounded implementation following
[T1](t1-template-rehearsal.md), on `feat/v1-template-patterns` after P1 `4bed3eb`.
The owner authorized local implementation/interfaces/staging/commits; no push is selected.
The [report](../reports/t2-template-patterns.md) records actual external consumer proofs.

## Supported compositions

| Configuration | Generated behavior |
| --- | --- |
| Required `applicationName`, `rootNamespace`; `catalogWorker` omitted or false | Editable .NET console host, Catalog Contracts/module, native PostgreSQL migrations, four existing foundation source snapshots and executable adoption tests. No worker project, Hosting dependency or hosted registration. |
| Same input with `catalogWorker: true` | Adds exactly one separate native Catalog report-worker executable and its process tests. It uses Catalog Contracts, an explicitly configured tenant/system deployment mandate and a fresh operation scope each iteration. No messaging or event-sourcing capability is selected. |
| Other keys, nonboolean worker input or transport/provider/pipeline choices | Rejected before creation. No partially generated unsupported composition. |

The CLI remains `create --config <json> --output <new-directory>`; selection lives in the
strict configuration file. Native `dotnet new` owns boolean conditional source/project/
package inclusion. The wrapper retains no-replace publication, new-directory refusal,
independent source snapshots and provenance. There is no repository-update mode, package
distribution change or generated runtime dependency on TypeScript/Foundry.

## Concrete Catalog capability

Catalog owns immutable complete item candidates: nonblank name up to 200 characters and
price 0–10,000 inclusive with at most two decimal places. The module's command Contract
creates an item or changes its name/price at an expected version. It requires established
identified actor/tenant and an already open caller-owned native transaction. It stages rows
only; Changed describes a proposal. Caller SaveChanges/commit establishes durability.

Candidate validation precedes persistence mutation. Missing visible items return NotFound;
stale observed versions return Conflict. Version is a native concurrency token, independent
of tenant ownership. Competing same-tenant decisions can both stage but only one saves;
the loser rolls back/disposes and may reload/redecide under consumer policy. No automatic
retry, save interceptor, generic repository or handler pipeline is introduced.

Catalog Queries/Filters expose untracked Contract results with literal case-sensitive name
substring, minimum price, matching filtered count and deterministic price/ID paging. Limit
is 1–100. Count/page are separate statements; consumers needing a common snapshot choose
their native transaction isolation. Arbitrary sorting, cursor paging and cross-module joins
are not added.

Retain T1's initial migration; add a forward price/version migration and update the native
snapshot. Host startup remains instructional and connects to no database. `migrate` is an
explicit finite action with no seed. Template-local business definitions remain consumer
source, outside Gloaming technical libraries.

## Selected worker and trust boundary

The report worker is an independently launched Generic Host BackgroundService, selected
only at generation. `--once` reports then exits; ordinary mode uses a cancellable periodic
timer (default 30 seconds; explicit interval 1–3600). It reads the current Catalog total and
first page size through its Contract, without changing business state.

Connection and tenant are mandatory deployment configuration. A consumer authorizes the
fixed `catalog-report-worker` system identity and selected tenant before deploying it.
The process establishes those contexts once per fresh iteration scope. Presence is not
admission/authorization, and no request-supplied identity or HTTP bypass is introduced.
Missing mandate/configuration exits 2; query failure exits 1 and emits no successful report;
successful once/normal SIGTERM completion exits 0. Native host cancellation owns shutdown.

This is useful worker composition evidence, not durable jobs, a queue, broker receipt or
exactly-once processing. It does not select a transport library merely to populate a preset.
The existing richer Wholesale business sample remains separate; generated Catalog is an
independent small consumer, not a copy of its business model.

## Proof scope

Create default Cedar, namespace-Task HarborDesk and selected-worker CedarWorker outside
Foundry beneath a conflicting SDK pin. Restore/build, native style/analyzers and actual
fresh PostgreSQL migrations/operations run on all three. Inspect generated/restored project
references and source hashes, reject escape paths and omitted event/messaging/worker state,
and compare omitted versus explicit false output except creation metadata.

Retain creator proofs for strict/duplicate input, unsupported combinations, occupied/repeated
destinations, partial engine failure and competing publication. Default adoption proves
tenant reads/write ownership and missing context, plus independent expected query results,
invalid candidate rejection, staged visibility, rollback and real root-version contention.
Worker adoption launches actual separate once/periodic processes, verifies tenant counts,
repeated fresh operation contexts, SIGTERM and fail-fast/missing mandate behavior.

## Extraction and limits

No new reusable mechanism is selected. Native EF queries/version predicates, existing
immutable contexts/ownership, DI and Generic Host suffice. The optional host shares its
editable composition registration with the console through ordinary IServiceCollection;
that small local reuse does not justify a new Gloaming pipeline or scope factory.

T2 proves template selection/omission and consumer pattern adoption. It does not prove
production authentication/admission, HTTP/OIDC/Access, durable worker state, provider or
transport alternatives, event sourcing, deployment packaging, library upgrades or repository
updates. Source-snapshot release/update/support costs remain explicit future decisions.
