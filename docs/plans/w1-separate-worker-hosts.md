# W1: separate worker hosts

Status: owner authorized implementation on 2026-10-09. No Gloaming interface changes.
Implementation is ready for owner review, uncommitted on `feat/separate-worker-hosts`;
[execution evidence and limits](../reports/w1-separate-worker-hosts.md).
Base: merged `origin/main`; this remote has no `origin/master`.

## Bounded outcome

Compose the existing Exports and Rendering modules in separate native .NET processes.
The producer API owns Exports only and commits accepted commands into its native outbox.
Independent worker roles dispatch that outbox, retain/acknowledge RabbitMQ deliveries,
and process Rendering's inbox. Existing Gloaming workers, row locks, leases and fresh scopes
remain the mechanisms. Transport and host lifecycle remain consumer code.

Run setup explicitly before starting the hosts. It applies the two existing migration sets
and declares a configured durable queue. Workers perform read-only prerequisite checks;
they never migrate, seed, create/delete queues or access peer module business state.
The processing role needs only Rendering's connection, with no broker or sender dependency.

## File and behavior map

- `src/Gloaming.Messaging/samples/MessagingProducerDemo/`: native HTTP draft/submission and liveness endpoints,
  Exports-only context/service composition, explicit native save/commit, no hosted dispatch.
- `src/Gloaming.Messaging/samples/MessagingWorkerDemo/`: explicit setup/dispatch/receive/process roles, host-owned
  native RabbitMQ connection/channel lifetime, existing adapter reuse and worker registration.
- `src/Gloaming.Messaging/samples/OutboxDemo/ExportDbContext.cs`: share its existing native EF configuration with
  both producer and dispatcher composition; preserve mapping/migrations and existing callers.
- `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/`: real process, PostgreSQL and RabbitMQ proofs using
  the existing fixtures. Database barriers establish in-flight work before process death.
- Solution, architecture checks, CI family command/path map: include the new consumer/proofs
  without creating another deployment test lane or reviving archive execution.
- Sample/family setup documentation, current roadmap/status and a dated slice report.

No library implementation/interface, event stream, migration, frozen archive or generated
template change. No worker registry, root runtime package, leader election, scheduling engine,
new transport abstraction, tracing/schema extension or template preset.

## Required proofs and limits

Prove explicit setup and no startup migration/seeding; API exit before delivery; retained intake
before broker acknowledgement and independent later processing; concurrent processor processes;
hard process death during processing rollback; death after confirmed publication followed by
lease expiry/redelivery/deduplication; graceful native SIGTERM shutdown and fresh-process recovery.
Use real SQL/broker observations, not arbitrary sleeps or mock acceptance.

The first topology uses one sequential operation per worker instance and a single configured
queue. Native RabbitMQ polling reuses the reviewed adapter; transport reconnection and poison
handling remain explicit later work. Failed intake exits nonzero; dispatch retains the existing
retry loop. Operators/supervisors own restart of failed intake or a permanently closed publisher
channel; no supervisor or automatic reconnect is supplied.
Queue retention, broker permissions/TLS, business authorization, replica policy and deployment
resource limits remain consumer concerns. W3C context/export belongs to OBS1.
