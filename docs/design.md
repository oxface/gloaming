# Library and template design decisions

Status: direction approved by the owner on 2026-10-03. Public interfaces, package divisions,
and implementation increments in [the extraction plan](plans/library-extraction.md) remain
review proposals.

## Purpose and ownership

The reusable libraries are named Rootbolt. Their namespaces, project/assembly names and
default package IDs use `Rootbolt.*`; the repository, sample and template identities remain
Modulith Foundry. [The rename report](reports/rootbolt-library-renaming.md) records the
mechanical scope and preserved persistence identities.

Build useful .NET libraries, a consumer-owned repository template, and a working sample.
Use the archived wholesale sample to identify guarantees and failure cases; reimplement
mechanisms when that gives a clearer design. Domain completeness is not the goal.

The template demonstrates one application composing business modules with explicit
Contracts, internal implementations, and separately owned persistence. Architecture checks
and policy middleware are reusable, opt-in tools with consumer-selected policies; the
library does not prescribe every consumer's project graph or module structure.

Authentication establishes identity. Independent actor-identity and tenancy contexts have
no dependency on each other or the sample Access model and do not grant authorization.
Membership, invitation lifecycle, roles, permissions, and business authorization belong
to the consumer.

## Current adoption strategy

For now, a library segment must also be usable in an ordinary .NET API or worker without
adopting the template's module layout or Access model. The template supplies a coherent
composition; standalone consumers receive the segment's documented guarantees through their
own explicit wiring. This is a current delivery choice, open to revision when implementation
evidence demonstrates value in more involved libraries.

Access starts as consumer-owned sample/template code, including the product's user,
Organization, membership, invitation and role behavior. Consumers can copy and customize it.
Shipping it in the template preserves a practical path to later extraction; it does not make
the Access model a dependency of the tenant/actor seam or other technical libraries.

Revisit either choice through [the plan's extraction and strategy gate](plans/library-extraction.md#extraction-and-strategy-gate).
The trigger is demonstrated reuse or complexity removed, followed by review of added policy,
coupling, customization and proof obligations. More involved integration is a possible outcome,
not a requirement to add speculative abstractions now.

## Tenant and actor meaning

Tenantless execution is an explicit choice, distinct from failing to establish a required
tenant. Each capability declares whether tenantless execution is permitted; operations that
require a tenant reject missing context. Login or tenant creation can run outside a tenant
boundary. Tenantless execution does not grant access to every tenant's data.

Keep the executing actor separate from the initiator. For deferred work performed by a
named workflow/system identity, that identity is the actor; the human who triggered it can
remain its initiator. Attribution grants no authority. Whether work continues after the
initiator loses membership or permission is an explicit consumer policy, not a decision made
by the context library. Consumers establish trusted actor identities and authorize operations.

Anonymous execution is explicitly represented, distinct from missing actor context. Consumer
policy decides which capabilities allow it; capabilities requiring a trusted human or
system actor reject anonymous or missing actor context. Anonymous and tenantless execution
are independent choices: a public operation may have a selected tenant, and an identified
actor may perform tenantless work. Context establishment itself grants no authority.

Tenant, actor and initiator stay fixed throughout one operation. Internal module calls may
share that immutable context. A worker processing different tenants or acting identities
establishes a separate operation context for each; it does not rebind its current context.

Use opaque string keys carried by distinct tenant/actor value types. Consumers explicitly
map their domain identities into these keys; the library does not prescribe GUIDs or depend
on authentication-provider identity types. Compare keys exactly using ordinal comparison.
Reject empty and whitespace-only keys; preserve accepted values without trimming, case
conversion or parsing. Consumers own canonicalization at the mapping point.
The representation trade-off is recorded in [ADR 0001](adr/0001-opaque-operation-identities.md).

Start the sample/template with a stable, globally unique application UserId and external
identities resolved by consumer-owned Access code. For OIDC, resolve validated issuer and
subject to that UserId; human actor keys carry the resulting application identity. Provider
resolution, account linking and email-based admission remain Access policy. The technical
library does not require provider/issuer fields or a User entity. Human and system actor
identities remain distinguishable even when their key text matches.

Operations obtain context through an injected, read-only accessor. The immutable context
value remains independent of DI, EF, web and transport types. Consumers wire context
establishment and accessor lifetime explicitly. The host initializes each required segment
exactly once per operation scope through its separate initialization interface. Reading before
initialization or initializing again is an error. Reset, replacement and automatic fallback
to anonymous context are excluded. Business code depends on the read interface; this
separation expresses consumer roles and does not replace application authorization.

Initiator attribution is optional. Consumers supply it when known; the library neither
infers it from the current actor nor automatically propagates it to another operation.
Scheduled maintenance and work with unavailable original attribution can omit it.

Once initialized, each accessor supports concurrent reads of its immutable context within
one operation. Required establishment finishes before parallel business work begins; separate
operations have separate scopes. This guarantee covers context reads and does not extend
the concurrency guarantees of other injected dependencies.

Provide explicitly called checks for context requirements, such as a selected tenant or
an identified actor. Consumers choose where to apply those checks and how to present
failures. The checks do not resolve membership, authenticate an identity, decide permission
or select HTTP responses.

The revised E1 interfaces and lifecycle are in
[the actor/tenancy slice plan](plans/e1-tenant-actor.md). The original combined interface was
reviewed and checkpointed as `a8e45c9`; the independent split was owner-reviewed and
checkpointed as `c8cbf64`. See [the split report](reports/e1-identity-split.md) for fresh proofs.

## Scope and HTTP integration review

`Rootbolt.ActorIdentity` covers executing actor kind/key, optional initiator, its
accessor lifecycle and explicitly invoked identity-presence checks. Actor denotes attribution,
not the actor concurrency model. `Rootbolt.Tenancy` covers selected tenant or deliberate
tenantless execution, its accessor lifecycle and selected-tenant checks. Both are package-free
and independent. Transaction, trace, HTTP and module-persistence contexts have their own seams.
Authentication, membership and authorization remain consumer behavior.

Capabilities needing both explicitly check both accessors. Each segment is established once;
there is no atomic publication across them. Hosts finish establishment of all segments needed
by a capability before invocation; an establishment failure aborts the operation and the host
disposes the scope. Consumers may establish actor identity first for tenant admission lookups.
Actor identity is kind plus canonical key; Access resolves profiles, external identities,
memberships and permissions without adding them to the contexts.

The HTTP direction below must be implemented with independently adoptable actor and tenancy
adapters; tenancy selection does not require the actor library. Consumer resolvers can compose
both when membership needs an application actor. Neither adapter is implemented in E1.

For HTTP usage, propose explicitly registered ASP.NET Core adapters with context
establishment middleware and consumer-selected tenancy defaults. The template should
require authentication through native authorization configuration and a selected tenant
through the tenancy adapter, with explicit endpoint/group exceptions. Registering the
adapter is an explicit consumer choice; the template's default is not a compulsory policy
of the core library.

Keep native authentication and authorization: `[Authorize]`, `.RequireAuthorization()`,
named policies, roles, schemes and `IAuthorizationService` retain their ASP.NET Core meaning.
The actor adapter can make established actor context available to consumer authorization handlers.
Identified actor presence is not a substitute for authentication or permission checks.
There is no parallel HTTP actor authorization policy or actor-specific endpoint extension.
Native authorization decides whether a caller may execute the endpoint; the resolver maps
the effective authenticated principal to an application actor. An authenticated principal
that cannot be mapped must fail establishment rather than silently become anonymous.
The core's explicit actor guards remain useful for capability invariants and non-HTTP calls.

Honor native `[AllowAnonymous]`/`.AllowAnonymous()` without a second actor opt-out, and propose
separate tenantless endpoint metadata for the tenant exception. An anonymous tenant catalog
can still require a tenant; authenticated tenant creation can require an
actor without a tenant. Login/health endpoints can explicitly allow both. Allowing anonymous
access must not replace an authenticated caller's resolved actor with an anonymous actor.
Public names for tenantless metadata remain under review.

Enforce tenancy requirements separately from native authorization-policy selection. Native
`FallbackPolicy` is not combined with named/default policies, and `AllowAnonymous` bypasses
authorization enforcement. The template uses native fallback/default policies requiring
authentication; named permission policies explicitly include their intended authentication
requirements. The adapter does not impose an extra actor rule when native authorization
permits anonymous access. Putting tenancy only in a fallback policy would leave gaps.
The adapter's tenant default must survive both named permission policies and anonymous access
unless the endpoint explicitly permits tenantless execution.
See [native policy selection rules](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0).
`HttpContext.User` remains available for claims and native APIs; the accessor supplies the
canonical application actor or tenant from the respective accessor for capability calls. It does not replace
`ClaimsPrincipal` or require consumers to abandon native identity access.

The consumer supplies actor mapping and tenant resolution/admission. Actor establishment
maps a validated external identity to the application actor. Optional tenancy utilities may
extract a candidate from configurable route values or hostnames; other strategies, including
one tenant per user, can use consumer resolvers. Selection, canonical identity resolution and
admission are separate responsibilities. Resolve and admit a tenant before initializing its
context; using a path or hostname does not grant membership. Strategies and any composition
precedence are explicit consumer choices, not an automatic fallback chain.
An OIDC claim is not automatically an application UserId or an admitted tenant. Resolution
may perform asynchronous Access lookups; it is not limited to copying claims. Anonymous actor
and tenantless cases are deliberate, independent outputs. Tenant business data can load later;
the context's tenant identity is never appended or changed after initialization.

Routing and native authentication precede context establishment; required contexts must
exist before handlers and any authorization handlers that consume them. Prove the exact
middleware order and failure behavior with the selected native authentication schemes.
In particular, scheme-specific authorization must not leave context based on a different
principal. Native challenges must remain intact. Consumers choose admission/failure responses
and keep relevant requirements/permissions at module entry points for non-HTTP callers.

Build the adapters alongside trusted HTTP ingress in E3. Review their public interfaces and
prove defaults, endpoint/group precedence, anonymous and tenantless exceptions, named policies,
authentication schemes, denied admission, challenges and login/callback/health paths before
claiming support. Include unmapped authenticated principals, retained authenticated identity
on anonymous endpoints, and native policies that deliberately permit anonymous access.
Both cores remain package-free; worker/message consumers initialize their own operation scopes
directly. E1 currently includes no HTTP middleware.

[The reviewed E3.1 scope](plans/e3-1-http-actor-identity.md) specifies the actor-only adapter
and consumer recipe, checkpointed as `faefc0b`. It wraps the native evaluator after policy authentication,
with downstream completion for requests without a policy, so establishment precedes consumer
authorization handlers without capturing a different default-scheme identity.
[The report](reports/e3-1-http-actor-identity.md)
records actual cookie/policy/claim-action proofs and provider limitations; the architecture
direction is recorded in [ADR 0002](adr/0002-explicit-http-actor-establishment.md).

For the first tenancy HTTP slice, the owner confirmed on 2026-10-05 that resolution/admission
runs after native authorization and actor completion, before endpoint work. Its requirement
metadata is independent of native authorization policies. Native challenge/forbid paths
short-circuit before tenancy resolution. This bounded composition enforces admission during
resolution and supplies established tenant context to downstream capabilities, not native authorization
handlers; handlers needing tenancy require a later integration slice. This does not add
a second evaluator or a runtime dependency on ActorIdentity. The implementation was owner-reviewed
and checkpointed as `cfbac9a`, with route/subdomain utilities and an executable
Organization/Inventory consumer. Selection presets are explicit registration-time choices,
and an opt-in options utility supplies native subdomain host filtering patterns. Consumer
global exception handling uses native ProblemDetails.
[The E3.2 plan](plans/e3-2-http-tenancy.md) specifies its interface;
[the report](reports/e3-2-http-tenancy.md) records fresh guarantees and remaining limits.

Membership/admission receives a separate consumer-owned Access increment in E3. One application
user may belong to multiple Organizations; admission concerns the Organization selected for
the current operation. Organization is the sample domain/UI term and Tenant the technical
isolation boundary. Invitations, membership management and role policy receive their own
reviewable increments; neither foundation library acquires these domain rules.
[The approved E3.3 plan](plans/e3-3-persisted-access.md) defines persisted lookup/admission:
fresh active-membership admission per operation, with immutable already-admitted work and
an explicit public catalog exception for anonymous callers and mapped non-members.
[ADR 0003](adr/0003-access-registry-and-operation-admission.md) records the consumer registry
and admission-time revocation direction; [the report](reports/e3-3-persisted-access.md) records
implementation evidence and remaining limits, checkpointed as `20a02be`.
[The owner-approved E3.4 slice](plans/e3-4-persisted-business-ingress.md) connects admitted ingress
to tenant-owned Inventory reads through populated module/Contracts projects, checkpointed with
E3.5 as `31c7a8b`. [ADR 0004](adr/0004-module-contracts-and-native-composition.md) records the business
Contract/native composition boundary; [the report](reports/e3-4-persisted-business-ingress.md)
records actual proofs. [E3.5](reports/e3-5-profile-mutation.md) adds protected Sales profile
edits with explicit versioned transactions. Its native antiforgery integration remains
consumer-owned; an optional human-actor requirement is the remaining extraction candidate.

OIDC/BFF registration starts as a small, editable sample/template helper in E3, composing
native `AddAuthentication`, `AddCookie` and `AddOpenIdConnect`. Provider settings, claim
mapping, session behavior, account linking and directory-gated/open admission stay visible
and consumer-owned. Promote focused authentication utilities only when exercised reuse
justifies a separate library. The later bootstrap CLI materializes the proven alternatives;
it does not turn authentication policy into a mandatory runtime layer. See
[native OIDC registration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0).

## Explicit control

For the initial EF proof, the owner confirmed one shared database, module-owned schemas and
tenant-discriminated tables. Ordinary tenant-scoped persistence rejects ownership changes;
any transfer is explicit consumer code. [E2.1](reports/e2-1-tenant-ownership.md) implements
explicit model registration and write validation with native ownership concurrency predicates,
proven for ordinary single-table string/GUID consumers on PostgreSQL and checkpointed as
`50e7933`. [E2.2](reports/e2-2-module-migrations.md) exercises consumer-owned native migrations
and history configuration for two module schemas without a new library mechanism,
checkpointed as `f2dcf2b`. [E2.3](reports/e2-3-tenant-relationships.md) adds an explicit native
same-tenant customer/address constraint as consumer-owned sample/template policy,
checkpointed as `d67c7fc`. [E2.4](reports/e2-4-versioned-profile-changes.md) adds an explicitly
advanced customer version and a two-save profile operation inside a caller-owned native
transaction, including stale-request, fault and cancellation proofs, checkpointed as
`6069c05`. The initial E2 scope is complete; E2.2 through E2.4 introduced no new reusable
mechanism. Shared cross-module transactions and the
remaining limits in [the E2 plan](plans/e2-persistence.md) require separate evidence.

Consumers register their DbContexts, entity mappings, migrations, handlers, transports,
queues, topics, subscriptions, and routes. Libraries may provide explicit registration
utilities and opt-in middleware whose effects and requirements are documented.

Use native EF transaction control where it suffices. A custom persistence scope must earn
its place by establishing a necessary protocol; document its entry, save, commit, disposal,
nesting, cancellation, and failure semantics. Saves and commits remain explicit. Middleware
does not silently commit, retry, compensate, or dispatch domain events.

Domain events are collected and processed deliberately. Integration contracts and their
mapping remain consumer-owned. Replay produces no historical external effects.

Supply callable delivery/maintenance operations separately from a simple optional hosted
worker. Consumers may host those operations themselves. Competing-consumer mechanisms need
their own claim, ordering, concurrency, and recovery evidence; leader election is excluded.

Library execution uses ordinary C# calls. Runtime code generation, a mediator, generic
repositories, and a generic workflow DSL are excluded. Existing archived EF migrations are
retained as evidence; native development-time EF migration scaffolding is allowed, with
generated migrations remaining consumer-owned and reviewed. Additional scaffolding choices
are stated in their slice.

## Dependencies and configuration

Start with explicit EF Core/PostgreSQL persistence and optional Rebus/RabbitMQ integration.
The owner-authorized [Messaging outbox](plans/outbox1-transactional-dispatch.md) keeps transport
implementations in consumer source. Used core/EF/provider layers concentrate enqueue/save
guards, PostgreSQL claims and optional sequential worker hosting. Each module owns its typed
context/table and final producer commit; dispatch commits its claim before publication and
fences completion with an unexpired token. No shared Rootbolt transaction runtime is introduced.
[ADR 0009](adr/0009-module-owned-transactional-outbox.md) records this direction. Outbox O1
is merged through PR #1 as `beafa4e`; durable inbox I1 is merged through PR #2 as `0f4d8bf`,
including intake-before-ack and separate local transactional processing described below.
Critter Stack is a behavioral reference for messaging and persistence, not a selected runtime
dependency. Keep provider-specific capabilities available; a provider-neutral promise needs
evidence from actual alternatives rather than a lowest-common-denominator interface.

Library segments are independently adoptable except for documented dependencies. Event
sourcing does not require messaging; messaging does not require event sourcing; state-stored
modules do not depend on event-sourcing types or registrations. Technical libraries do not
reference sample module Contracts. Use project references during local development.

[E4](plans/e4-event-serialization.md) implements `Rootbolt.Events.Serialization`
with native JSON and explicit durable name/version registrations, independently of every
other segment. Consumer event definitions, JSON requirements/defaults and domain meaning
stay outside the codec. [Its report](reports/e4-event-serialization.md) records fresh
two-family compatibility and dependency proofs; the owner-reviewed implementation is
checkpointed as `2a49ef3b`. Codec support does not establish event-store or delivery guarantees.

The owner-approved [payload-upcasting extension](plans/event-payload-upcasting.md) adds explicit
forward JSON transformations and a PassThrough helper to the optional codec. Current writes
retain one registered identity per CLR type. Every declared historical source must resolve to
an exact registered terminal; no inferred fallback, alias rewrite or stored event mutation is
introduced. Consumers own transformation meaning, native JSON policy and reader-before-writer
rollout. Inventory exercises mixed JSONB schemas through the existing history reader and
independent rebuilding; a standalone adopter exercises chained/nested shapes. The EF adapter
and Events.History gain no Serialization dependency. [New evidence](reports/event-payload-upcasting.md)
is separate from the earlier codec/history/store checkpoints.

[E5.1](plans/e5-1-event-history.md) implements independently adoptable
`Rootbolt.Events.History` for consumer-selected ordered-range validation. The owner
approved removing the initial complete-history snapshot/selection object. The utility has
no payload generic, JSON or other segment dependency; consumers explicitly select/materialize
rows, validate metadata, decode and hydrate. The strict range policy requires contiguous
versions and nondecreasing recorded timestamps within the returned range, allowing equal times.
[The report](reports/e5-1-event-history.md) records two-family proofs and remaining storage
limits. Its final package division remains provisional until E5.2's native EF consumers.
Selected-range validation does not certify excluded rows, a captured database head, commit
order, tenant scoping or concurrent append behavior. The revised implementation was
owner-reviewed and checkpointed as `4cc12a1`. [E5.2.1 database consumers](plans/e5-2-1-native-event-history.md) implement native EF reads
in owning Inventory/Purchasing modules, with [fresh PostgreSQL evidence](reports/e5-2-1-native-event-history.md),
owner-reviewed and checkpointed as `4f4d5b2`. No new Foundry mechanism or public interface is
added. The range
validator now serves two real database readers; retaining its separate library is the review
recommendation, not a frozen package decision. Native mappings, tenant/stream constraints,
query selectors, timestamps and domain evolution remain consumer/template policy. Captured
heads bound reads under an atomic append-only assumption.
[E5.2.2](plans/e5-2-2-native-event-append.md) implements module-owned command decisions and
expected-version staging in caller-owned native transactions, checkpointed with E5.3 as `abcd370`.
[Its fresh PostgreSQL proofs](reports/e5-2-2-native-event-append.md) cover competing creation/
append, complete-batch rollback, two explicit saves and fresh-context recovery. No new Foundry
mechanism was needed: EF's original-version predicate and native constraints arbitrate writes.
The repeated staging shape remains a comparison candidate for E6's required participants;
decisions, result types, row mappings and narrow native fault classification stay consumer-owned.

Owner review identified a narrower extraction candidate in the two duplicated HistoryMapping
implementations. [E5.3 explicit event-sourcing storage registration](plans/e5-3-event-storage-registration.md)
was owner-reviewed and checkpointed as `abcd370`. Consumer rows keep their concrete
types and optional ownership fields;
an explicit EF utility configures shared stream/envelope tables, version concurrency and
selected keys/relationships/position uniqueness. Schema/table options and native builders
keep setup editable. Ownership, provider payload mapping, DbContext, migrations and saves
remain explicit consumer choices. The segment requires neither tenancy nor other
event libraries. [The fresh report](reports/e5-3-event-storage-registration.md) proves adoption
by both modules without schema changes and customized tenant-free mixed-type storage in an
independent executable. It adds a reusable mapping utility, not append/transaction orchestration.

[E6.1](plans/e6-1-inline-decision-state.md) adds sample-owned inline decision state and required
views, checkpointed separately as `1ae13d4`. Commands load version-checked state without replay;
explicit live/temporal reads retain their reconstruction behavior. Prepare the complete batch,
including all serialized view/event payloads, before tracking mutations. Purchasing evolves
its summary from its own committed amounts, independently of the aggregate-shaped write view.
Native view concurrency tokens and the stream token arbitrate writes; the caller owns the
transaction and rollback. Missing or behind required views fail without automatic repair; an
ahead view after an older header read is a concurrency outcome. [The report](reports/e6-1-inline-decision-state.md)
records real PostgreSQL proofs. The small checks and native staging remain editable module code;
no shared projector, aggregate base or transaction mechanism was justified. Bounded repair is
E6.2, and audit/messaging require their own participants and proofs.

After the owner-approved T1 template rehearsal (`8ccf4c8`), ES1 concentrates one bounded
append capability. The owner reviewed the initial interface, then requested and approved
an aggregate/store replacement. The package-free EventSourcing core provides the required
write contract and optional atomic candidate/pending bookkeeping. The existing optional EF
segment adds a configured appender/typed record adapter and references that core; see
[ADR 0005](adr/0005-aggregate-write-contract-and-native-append.md).

The appender owns GUIDs, ordered positions, one configured-clock sample, payload lifetime,
complete mapped keys and native header staging. After further owner feedback, the owner
endorsed IEventStore<TAggregate> and authorized implementing the concrete provided store for
review. [Its exact surface and change map](plans/es1-library-write-store.md) concentrate scoped
stream lookup, family/version validation, observation/transaction binding, validated main-state
loading and configured required-state staging. Consumer bindings supply ownership, state
encoding/reconstitution; handlers pass the aggregate only. Earlier secondary-view support
was replaced by the owner-reviewed ES2 aggregate-only reduction below.

Domain operations, pure reducers, eligibility, business Contracts, tenant admission and native
save/commit remain consumer-owned. Create produces an opening fact immediately. ApplyChanges
changes accepted aggregate bookkeeping; Evolve computes pure whole-batch state, reusable in the
owning module. JsonElement stays in persistence bindings. No history/projector engine, repair,
async processing or template event preset is added. The raw counter reuses captured history
and direct JSON without required inline state. T1's event-free composition remains intact.

[ADR 0006](adr/0006-transactional-main-inline-state.md) requires main state for registered
aggregate writes and permits raw streams. Explicit native model declarations plus validation
in both save overrides enforce tracked participation/version/time/complete event range for
the one required aggregate state. This does not enforce arbitrary SQL or validate
semantic equivalence of manually changed state bodies. Native module Queries and Filters
remain the read side; a PostgreSQL availability query demonstrates the mapped-state boundary.

[The follow-up slice report](reports/es1-library-write-store.md) distinguishes new store/save
proofs from [prior appender evidence](reports/es1-bounded-event-append.md) and the archive.
Exact implementation and configuration remain available for line-by-line owner review; edits
are unstaged and existing staged entries are preserved. The
[deferred catalog](plans/event-sourcing-capabilities.md) records separate future proof obligations.

The owner requests self-sufficient documentation alongside each library. Local READMEs and
capability records define current behavior, dependencies, limits and deferred context; root
plans/ADRs/reports record review and historical executions. The
[event-store capability record](../src/Rootbolt.EventSourcing/Rootbolt.EventSourcing.EntityFrameworkCore/docs/capabilities.md)
carries the supported write-only store, independent full-replay rebuilder and native header
Version/ConcurrencyStamp contract. [ADR 0008](adr/0008-native-optimistic-aggregate-rebuilding.md)
replaces [the historical pre-read gate](adr/0007-pre-read-admission-for-inline-rebuilding.md).
Repair preserves facts/head/time and changes the technical stamp; stale decisions and competing
repairs lose through native save predicates. Writing requires no history or maintenance binding.
Final save/commit, typed contexts, tenant admission, reducers and scheduling remain consumer-owned.

The owner-approved [bounded history reader](plans/event-history-reader-extraction.md) now provides
complete-key, parameterized ordered-prefix queries, captured-version bounds and integrity checks
inside the EF adapter. It reuses the existing Events.History utility; that dependency is explicit
and the utility stays independently adoptable. Consumers supply payload decoding, temporal target
selection and pure evolution. The independent rebuilder injects a reader rather than exposing a
history-loading override. No serialization/provider dependency or automatic read repair is added.

[The reviewed replacement](plans/es2-native-ef-simplification.md) removes public prepared handles,
admission bookkeeping, the provider package and dormant Purchasing summary storage. Previous
migrations remain; new migrations add header stamps and remove that active legacy table. Queries
and summaries use native EF projections/filters over the one current inline aggregate. Consumers
may reconstruct live views on access through existing readers/reducers. No automatic repair or
catch-up occurs in ordinary loads. Secondary/async/multi-stream projections remain deferred until
native query shapes are insufficient. A maintenance worker is still needed eventually, deferred
to keep this change focused; consumers can host reconciliation and own locking/windows, retry,
scheduling and scaling. Family grouping remains checkpointed as 39c1ab3; T1 stays event-free.

Use options for supported variations, and explicit registration or dependencies for larger
policy changes. Document how a setting changes a guarantee. Schema names, transport routes,
worker hosting, and application policy remain consumer choices.

Keep the email replacement seam narrow; SMTP/MailKit does not become a mandatory foundational
dependency. Authentication, general notifications, and a transport/provider matrix are not
automatically promoted into libraries.

## Template bootstrap direction

The owner-approved [T1 rehearsal](plans/t1-template-rehearsal.md), checkpointed as `8ccf4c8`,
now materializes one ordinary consumer-owned Catalog/console repository using native
`dotnet new` with a TypeScript/npm creator. Application name and root namespace are configured
explicitly; three existing libraries are materialized as local source snapshots. The fixed
composition uses PostgreSQL state storage and omits events and messaging. See
[creation requirements and invocation](../tools/template/README.md) and
[execution evidence](reports/t1-template-rehearsal.md).

This bounded source-distribution choice does not settle package releases or upgrades. Initial
creation refuses existing destinations; updates, additional platforms and richer compositions
remain separate capabilities. Further template work can select reviewed alternatives and
optional capabilities. Intended choices include transport, event sourcing,
Aspire resources and authentication/admission setup such as Keycloak with directory-gated
or open registration. These are configuration goals, not currently supported alternatives.

Introduce an option after its implementation and composition have been exercised. Provider
selection does not require a common provider abstraction. Authentication/admission choices
configure consumer-owned Access policy. T1 supports the documented initial-creation behavior
only; broader combinations and repository-update semantics remain later E10 decisions.
The population tool introduces no dependency into the generated application runtime.

## Telemetry and tooling

Libraries use native `ILogger`, `ActivitySource`, and `Meter`. The host's ServiceDefaults
configures collection, export, and health. Aspire is local orchestration and topology-test
infrastructure, not a required library runtime.

[ADR 0012](adr/0012-durable-diagnostic-context-and-native-telemetry.md) records optional retained
W3C messaging context, links at native attempt creation and diagnostic-independent inbox
deduplication. [Local observability setup](../src/Rootbolt.Messaging/docs/observability.md)
keeps SDK/exporter, sampling and transport decisions in consumers; transaction ownership is unchanged.
The optional `Rootbolt.Messaging.OpenTelemetry` adapter supplies native trace/meter builder
subscription helpers, with an API-only dependency and no host/provider/exporter policy.

[E3.6](plans/e3-6-runtime-composition.md), checkpointed as `28ee797`, adds sample-owned AppHost
and ServiceDefaults source,
with native database references, manual finite setup, distinct readiness/liveness and OTLP
export. Startup never applies migrations or seeds. Its health policy is host-owned and no
global retry/resilience handler is installed. The owner confirmed native ServiceDefaults
supplies sensible defaults and belongs in template source, with no Foundry library wrapper.
The [E3.7 real OIDC/browser slice](plans/e3-7-oidc-browser-journey.md) was owner-reviewed and
checkpointed as `dc3ac3b`. Its optional native Keycloak resource, explicit external identity
setup and cookie/OIDC challenge composition stay editable sample/template code.
[The report](reports/e3-7-oidc-browser-journey.md) records actual browser journeys and their
local HTTPS topology limits. No new reusable authentication mechanism is extracted.

Retain `.editorconfig`, CSharpier, Lefthook, and commitlint. Introduce a pinned pnpm workspace
and shared frontend subpackages with the first exercised frontend setup; existing npm-based
repository tooling remains until that deliberate transition. CI remains authoritative.

## Evidence and supported limits

Each library increment supplies tests through its public interface and real sample usage.
Transfer the intent of archived proofs, with independent expected outcomes, rather than
equating matching implementations with correctness. The same sample domain permits useful
comparisons without freezing its old implementation.

Shared database transactions, separate commits with compensation, and durable sagas are
different guarantees. Cross-module shared transactions still require a dedicated proof;
in-process orchestration alone provides no recovery after process death.

Historical identity, repair, replay-cost, retry, and shutdown limits remain visible until
new evidence resolves them. Production provider compatibility, packaging, deployment, and
recovery are separate proof gates, not consequences of local extraction.

## Library documentation and family ownership

B1 closed on 2026-10-14 as the
[retention and contract assessment](reports/b1-retention-and-contract-assessment.md).
The owner rejected retaining the experiment code, tests, migration or reconstruction patch.
Only research and [dated execution results](reports/b1-replayable-integration-feed.md) remain;
[ADR 0015](adr/0015-producer-owned-retained-profile-feed.md) records the rejection.
Common Sales has no feed API, gate, mapping or activation. No Rootbolt feed interface or
template preset was extracted. Public-state revisions require explicit producer semantics;
DDD and private event history do not establish generic integration-event replay or ordering.
Revisit state transfer only for a later concrete receiver need; B2 implementation remains separate.

The owner-approved WF2 replacement provides optional bounded durable continuations and a typed
native EF runner. The provided process row retains initial input, consumer state and requested
operations/outcomes, with scalar version concurrency. Only the saved continuation executes;
ordinary consumer methods choose branching and compensation. The runner enqueues one returned
request in the owning transaction. Admission, Contracts, business validation and native save/commit
remain consumer-owned. Existing inbox/outbox workers execute delivery; no workflow scheduler,
replay engine or generated harness is introduced. [ADR 0014](adr/0014-bounded-durable-continuations.md)
supersedes the uncommitted sequential experiment; [the local contract](../src/Rootbolt.Messaging/docs/workflows.md)
records supported behavior and limits.

Active ActorIdentity, Tenancy, Persistence, Events and EventSourcing families group their
independently selectable projects, README, local capability/deferred context and library tests
under `src/Rootbolt.{Family}/`. Detailed package setup remains with each project. Root
ADRs/plans/reports keep cross-cutting decisions and dated evidence; consumers can discover
essential integration obligations beside the library. Shared architecture/support and sample
proofs stay with their owners. See [the relocation scope](plans/library-family-layout.md).

## Durable incoming work and local processing

I1's interface and scope received owner approval on 2026-10-08. Each receiving module owns its
typed native context and inbox table. Transport adapters commit retained intake before native
acknowledgement or successful HTTP acceptance. Delivery identity is subscription/producer/ID;
incompatible reuse fails explicitly. Independently invoked processing holds a PostgreSQL row
lock through bounded local handler work, native save, completion and commit; outgoing effects
are explicitly queued in that transaction. This avoids inbox leases without changing the outbox's
external-publication lease protocol. No transport/ack facade or common Rootbolt unit of work is
introduced. [ADR 0010](adr/0010-durable-inbox-local-processing.md) records the settled boundary;
[the family contract](../src/Rootbolt.Messaging/docs/inbox.md) gives standalone setup and limits.

## Explicit accepted-change audit

The owner approved [E9's public interface and bounded consumer scope](plans/e9-explicit-transactional-audit.md)
on 2026-10-09. `Rootbolt.Auditing.EntityFrameworkCore` explicitly captures consumer-supplied
ActorIdentity attribution and owned JSON, maps a provided row and validates staged envelopes
against their owning native context/transaction. No hidden save/commit, context fallback,
automatic entity auditing or product classification is introduced. Native DI stays consumer
wiring; only ActorIdentity and EF Relational are dependencies of the provider-independent EF
package. The optional `Rootbolt.Auditing.EntityFrameworkCore.Postgres` package composes its
mapping with JSONB details. Sample wrappers capture actor/tenant, generated ID and clock;
business calls provide accepted changes. SubjectKey is
nullable for events without an individual item. ConfigureAudit supplies the portable subject
timeline index by default; consumers retain native customization. Named
V1 payload records keep schema constants beside their fields in the sample wrappers.
The owner subsequently removed correlation, causation and trace IDs from the audit envelope
under YAGNI. Audit is business history; diagnostic telemetry and existing messaging metadata
retain their separate seams. Neither sample audit wrapper needs request/message context.

Sales profile changes and Inventory inbox stock issues exercise accepted-change participation
in their existing transactions. Business selection, Human/System trust, action/reason vocabulary,
detail disclosure, query visibility and retention stay consumer-owned. Denial auditing and
cross-module atomicity remain separate proof gates. [ADR 0011](adr/0011-explicit-transactional-audit.md)
records this boundary; [the library-local contract](../src/Rootbolt.Auditing/docs/transactional-audit.md)
and [E9 report](reports/e9-explicit-transactional-audit.md) document current evidence and limits.
