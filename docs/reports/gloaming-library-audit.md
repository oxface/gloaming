# Gloaming library audit and cleanup queue

Updated: 2026-10-23. Audit started at `1582e77849088e243881c82a37c0a42b2a11a322`.
Current branch: `docs/gloaming-library-audit`, in the primary repository checkout.
Guidance/reference checks are checkpointed as `bd2adb7`; evidence/proof consolidation as
`3c4f9a9`. [Integrated verification and independent reviews](integrated-verification-and-reviews.md)
passed locally against that checkpoint. The extra [documentation/test pass](documentation-and-test-cleanup.md)
and verification reports are checkpointed as `c5acb8e`, after all normal hooks passed.
The current uncommitted batch is the [original-request sweep](original-request-sweep.md),
requested before the owner's separate review. Its local checks pass; all new changes await owner review.
This is the only active cleanup queue; dated plans/reports do not authorize new work or commits.

## Confirmed direction and review criteria

Gloaming is the primary product: reusable libraries with explicit consumer authority.
A selected operation may own a bounded transaction and invoke consumer code when its guarantees
and obligations are clear. Judge each interface by complexity removed after configuration,
ordering, scope, failure handling and support cost, not by source size or registration opt-in alone.

Consumers retain business rules, authorization/admission, module Contracts, payload meaning,
operational/deployment policy and persistence control outside deliberate delegation. Fixed
technical tables and provider-specific protocols can be useful library choices with clear limits.
No external application/database/message compatibility constrains the current cleanup.

Umbrine is the next real consumer: modular-monolith ERP, isolated module persistence,
multitenancy and durable business processes. This cleanup does not implement it. Human assignment,
reminders, deadlines and richer workflow protocols require concrete consumer needs before any
new extraction. The existing bounded continuation runner does not establish that broader model.

Application patterns replace configurable template development. Future skills should automate
repeated real work without duplicating architecture rules. No replacement generator, bootstrap
CLI, topology matrix, authorization engine or general workflow framework is selected.

## Library dispositions

The current inventory is **21 packages across nine families**. [Family entry points](../../README.md)
and local contracts define the actual dependency closures and limits. The
[original package-by-package assessment](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/gloaming-library-audit.md#package-dispositions)
records all 23 initial packages and the evidence behind these choices.

| Area | Implemented disposition and boundary |
| --- | --- |
| ActorIdentity and Tenancy | Keep independent immutable operation contexts and optional native HTTP/PostgreSQL integration. They grant no authorization. |
| Persistence and shared EF saving | Keep selected ownership/RLS and ordered preparation/validation. Supported mapping/credential/transaction limits stay explicit; no universal unit of work. |
| Events and EventReactions | Keep independently selectable codec/history and composable ordered local reactions. Domain types, payload evolution and business dependency ordering stay consumer-owned. |
| EventSourcing | Keep one required inline aggregate, native direct staging/optimistic stamps and explicit full replay/regeneration. Secondary/async/multi-stream projection engines need separate evidence. |
| Auditing | Keep explicit transactional staging; replace the one-line PostgreSQL mapping package with native JSONB configuration. |
| Messaging persistence and hosting | Keep callable inbox/outbox/maintenance; isolate optional sequential polling in its Hosting package. No mandatory worker dependency for producers. |
| Continuations | Combine definitions and execution in the existing optional EF package, retaining public namespaces. A future provider-free definition requirement must justify another seam. |
| Telemetry and native RabbitMQ | Use native source/meter subscriptions; retire the thin OTel adapter. Keep optional native RabbitMQ resource lifetime mechanics separate from routes/restart policy. |

No generic router, mediator, discovery runtime, provider matrix or framework feature-parity
work follows from these dispositions. [Research briefs](../research/README.md) retain useful
alternatives and primary-source provenance; [ADRs](../adr/README.md) record current trade-offs.

## Implementation progress

The owner requested subsequent increments in this checkout and branch. Earlier isolated
worktrees are recovery copies, not alternative active review locations. Every checkpoint below
has its own owner-approved change set. Their results are dated evidence and must not be summed
or treated as fresh verification of the final tree.

| Checkpoint | Outcome | Dated evidence |
| --- | --- | --- |
| `d63f567` | Ordinary local package command; native audit/telemetry setup; two thin adapters retired. | [Local packages](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/local-package-development.md), [adapters](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/thin-adapter-retirement.md). |
| `ffc6ec2` | Optional messaging worker hosting separated from persistence. | [Hosting](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/messaging-worker-hosting.md). |
| `56a8b12` | Continuation definitions/execution combined; 21-package inventory. | [Consolidation](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/continuation-package-consolidation.md). |
| `5d86b20` | One focused Messaging delivery example; fault/deployment hosts become test-owned. | [Combined audit record](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/gloaming-library-audit.md#messaging-example-and-fixture-consolidation). |
| `dd6eb77` | Generator, skeleton, generated-only CI and ContextDemo retired; direct native-DI proof retained. | [Proof transfer](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/gloaming-library-audit.md#application-scaffolding-retirement). |
| `ae2909c` | Focused ownership, codec, counter and immutable reaction examples; larger variants remain test-owned. | [Family proof map](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/gloaming-library-audit.md#remaining-family-example-consolidation). |
| `8601204` | Wholesale Purchasing retired; unique event-store assertions transferred. | [Retirement](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/gloaming-library-audit.md#wholesale-purchasing-retirement--2026-10-23). |
| `0d43d3c` | Dispatch/directory policy retired; populated Sales upgrade and route-exclusion/grant proofs retained. | [Retirement](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/gloaming-library-audit.md#wholesale-dispatch-retirement--2026-10-23). |
| `bd2adb7` | 80 dated plans retired; current design/gate; tested reference checks in CI/hooks. | [Guidance and checks](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/repository-guidance-consolidation.md). |
| `3c4f9a9` | Dated reports/retired decisions consolidated; four research briefs, current ADR/proof maps and 84 retained architecture cases. | [Evidence and proof ownership](evidence-and-proof-consolidation.md). |
| `c5acb8e` | Independent EF coordinator tests, current test navigation, consolidated capability guidance and integrated verification/review records. | [Documentation/tests](documentation-and-test-cleanup.md), [integration](integrated-verification-and-reviews.md). |

Wholesale now contains Access, Sales and Inventory with native API/Worker composition and
one order/reservation journey. Focused family examples are public adopters; compatibility,
operator/fault and separately compiled process fixtures are test-owned. No sample rule is
promoted merely to preserve a historical scenario or test count.

The latest broad sample checkpoint passed 622 Wholesale cases, 89 architecture cases,
108 outbox cases, 93 inbox cases and six native runtime/browser cases. The guidance checkpoint
passed six repository-tool cases and all 21 archives/six isolated NuGet consumers; its 868 owned
source/project/fixture paths were unchanged. Exact commands, attempts and limits are in the
pinned reports. These are historical checkpoint results, not new executions by this document.

Checkpoint `3c4f9a9` reduces architecture to 84 cases by removing five provider-test package-list
snapshots, keeps the actual library declaration rules, and removes duplicate Worker references.
It distills research, reconciles ADRs/current family wording and retires 129 dated reports plus
four superseded/rejected/retired ADRs. Its [slice report](evidence-and-proof-consolidation.md)
records fresh results, preservation checks and the precise retirement/proof map.
**No new reusable Gloaming mechanism is proven by this consolidation.**

Fresh integrated verification of `3c4f9a9` passed all **30 native projects / 1,671 cases**, with
zero failures/skips, plus **21 archives / six isolated package consumers**. Separate Standards,
Spec and architecture reviews found no actionable issues in the fixed audit diff. The
[integrated report](integrated-verification-and-reviews.md) records scope, evidence and limits;
these checks do not replace the extra documentation/test pass or publication sign-off.

The extra [documentation/test pass](documentation-and-test-cleanup.md) gives the existing EF
save coordinator independent tests, retains real cross-family saving proofs and family-local
placement, improves test discovery and repairs duplicated/stale guidance. Fresh checks pass
184 affected native cases, solution/style/analyzers/references and all 21 archives/six package
consumers. Runtime library/sample code and owned migrations/literals are unchanged. No new
runtime mechanism or deferral is introduced; the batch was owner-approved and checkpointed as `c5acb8e`.

The [original-request sweep](original-request-sweep.md) repairs an archived VS Code launch target,
the missed base EF package in dependency-update grouping, a removed heading link and the
ServiceDefaults ownership description. It strengthens the existing application recipe without
another generator or skill. Static/reference checks and affected README packing pass; all 870
owned source/project/fixture inputs remain unchanged. No further library split or reusable
runtime mechanism is justified by this pass.

## Current proof owners

Use [the test map](../../tests/README.md) for all 31 native projects and their distinct
unit/provider/adoption/process obligations. [Retirement provenance](s1-archive-retirement.md)
records historical origins. Family-local limitations and explicit deferrals remain authoritative.
Current owned migrations/literals stay with their consumers; retired consumer policy needs no
replacement fixture. Source links or a matching test count do not establish a passing execution.

The [local package command](../../tools/release/README.md#everyday-local-packages) supports
ordinary development consumption now. Unique versions and consumer source mapping avoid
stale package caches. The heavier verifier proves archives and six fresh-cache package-only
profiles; remote publication is a separate owner action.

## Remaining cleanup slices

| Required work | Completion evidence |
| --- | --- |
| Fresh-context sweep | Separate session planned by the owner. Do not count its conclusions as evidence already supplied here. |

After review/checkpoint of the original-request repairs, the remaining planned cleanup is the
owner's **separate-session fresh-context sweep**; further increments depend on its findings, with no fixed cap. Important
defects remain required until fixed or explicitly disposed of by the owner. The five approved inbox-retention supplied-feature deferrals
remain in [the owning contract](../../src/Gloaming.Messaging/docs/inbox-retention.md); no new
deferral is introduced. Production capacity, Umbrine implementation, push and publication are
separate from this cleanup and are not claimed by successful local proofs.
