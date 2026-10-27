# Select Gloaming capabilities

These patterns work with native .NET composition. Add a Gloaming capability only where its
documented guarantee removes a technical obligation in the consuming application.
The package README is authoritative for the version in use.

## Package integration

For local library development, pack a unique Gloaming snapshot and consume its completed folder
feed through NuGet source mapping and exact PackageReferences. The
[local-package guide](https://github.com/oxface/gloaming/blob/main/tools/release/README.md)
covers source mapping, cache behavior and the package-only verifier.

If this folder is copied without the library checkout, use the published contract/source links
below. When the matching source is available locally, inspect it and its executable examples.
Check the chosen package version's API, target framework and native provider dependencies.
Keep application project references inside the application; consume technical libraries as packages.

### Distribution boundary

Hosted distribution remains deferred while the early package graph evolves. The
[local-package distribution contract](https://github.com/oxface/gloaming/blob/main/tools/release/README.md#distribution-boundary)
explains exact archive retention, changing package sets and the absence of a source-rebuild
byte-reproducibility guarantee. Pin package versions and source revision together, and prove
locked restore from an initially empty package cache using the chosen archive source.

If a consumer temporarily rebuilds pinned source, document its source/version/toolchain,
credentials and removal condition, and prove independent clean builds against the committed
lock hashes. This bridge is consumer-owned and does not establish upstream reproducible-build
support. Never normalize signed or already published packages. Local snapshots remain usable
for early adoption; disclose the remaining clean-CI distribution obligation.

### Read contracts for the adopted version

Record the exact Gloaming package version and source revision in the application's setup
documentation. Start with the README included in that package. Read the `repository` element's
`commit` attribute in the `.nuspec` inside the `.nupkg` archive to identify its source revision;
packages from one local snapshot should agree. The local version's random suffix is not a Git
revision.

The published source links in these patterns point to `main` for navigation. For the adopted
snapshot, replace `/blob/main/` with `/blob/<recorded-commit>/`, or inspect that revision in a
separate local checkout. Use the matching contracts and examples when implementing API calls,
model configuration and save guards. Later changes on `main` do not change an already packed
snapshot.

For one protected EF/PostgreSQL operation, the [first-module walkthrough](first-module.md)
identifies the selected packages and executable examples.

| Requirement | Contract and selection |
| --- | --- |
| Actor/initiator attribution | [ActorIdentity](https://github.com/oxface/gloaming/blob/main/src/Gloaming.ActorIdentity/README.md), with optional native HTTP establishment |
| Selected tenant/tenantless context | [Tenancy](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Tenancy/README.md), independent of actor identity |
| Ownership/protected saves and optional RLS | [Persistence](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Persistence/README.md) |
| Ordered native save preparation/validation | [EntityFrameworkCore](https://github.com/oxface/gloaming/blob/main/src/Gloaming.EntityFrameworkCore/README.md) |
| Event-sourced aggregate writes/replay | [EventSourcing](https://github.com/oxface/gloaming/blob/main/src/Gloaming.EventSourcing/README.md) |
| Stable JSON event names/upcasting/history | [Events](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Events/README.md) |
| Local fact capture/reactions | [EventReactions](https://github.com/oxface/gloaming/blob/main/src/Gloaming.EventReactions/README.md) |
| Explicit accepted-change audit | [Auditing](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Auditing/README.md) |
| Logical action/result retention | [CommandReceipts](https://github.com/oxface/gloaming/blob/main/src/Gloaming.CommandReceipts/README.md) |
| Durable inbox/outbox and selected continuations | [Messaging](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Messaging/README.md) |

## Integration obligations

Bind registrations and model/save configuration to the same typed context. For every selected
capability, account for native sync and async saving, required transaction isolation,
failure disposal and its supported mapping constraints. Use the save coordinator where its
ordered phases help; it does not create a generic transaction middleware or silently commit.

Establish required immutable actor/tenant context before resolving protected scoped persistence.
HTTP and worker ingress have different admission paths. RLS also requires native policies,
restricted credentials and transaction-local setup for reads as well as writes.

Keep explicit mappings, connection/credential roles, migrations and deployment ordering in the
application. Library scope or tenant metadata does not authorize access. Select transport-specific
packages only for the routes that need them.

## Reference application

[Wholesale](https://github.com/oxface/gloaming/blob/main/samples/Wholesale/README.md) shows native
composition across Access, Sales and Inventory. Use its wiring and proof boundaries as examples;
application users, grants, stock rules, SQL roles and exact module graph are its own policy.
