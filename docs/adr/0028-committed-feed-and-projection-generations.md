# 0028: Explicit committed feed and secondary projection generations

Date: 2026-10-24. Status: implemented for owner review.

## Context and decision

Bounded per-stream history and explicit full aggregate replay do not provide a complete cursor
over multiple streams. Allocated identities, timestamps and transaction-start order can skip a
late commit or reverse actual stream publication. Two native consumers need retained history,
atomic projection progress, restart and a separately selected visible generation.

Add an optional `Gloaming.EventSourcing.EntityFrameworkCore.Postgres` module, independent of the
provider-free aggregate/store packages. The owner explicitly selected strict feed ordering with
its transaction writer-lock cost. Consumer-reviewed migration SQL locks a fixed source set,
copies a synthetic baseline and installs capture triggers. A transactional head row serializes
capturing transactions until commit/rollback. Native transaction identity groups work only.
Persisted complete key text uses `C` collation, avoiding locale-dependent JSONB comparison.

The public seam consists of explicit bounded read, one projection turn and conditional activation.
A fresh consumer-established scope and ReadCommitted transaction own cooperative reducer staging,
native save, exact checkpoint update and commit. A generation lock excludes duplicate workers.
Unacknowledged commit yields Indeterminate without automatic callback retry; fresh retained state
determines recovery. Poison input stops progress. Cleanup diagnostics preserve known commits.

Activation fences writers and verifies catch-up before changing an expected active pointer.
Already active is an idempotent observation despite subsequent lag. Consumers qualify output
rows by generation and ownership; readers join pointer and output in one statement/stable snapshot.
Original historical cross-stream commit order cannot be reconstructed during populated bootstrap.

## Ownership and consequences

The module owns capture/cursor/checkpoint/activation protocol locality. Consumers own source and
output migrations, admission, typed readers, reducer meaning, workers, trusted credentials,
deployment, supervision, mixed-reader migration, draining and retention. The initial source model
is ordinary append-only tables without RLS/inheritance/partitioning/rules. Runtime writers do not
own feed objects; fixed security-definer capture runs as a restricted migration owner. No startup
DDL, projection fleet, message runtime, automatic source expansion or history deletion is selected.

The deletion test would redistribute the same commit-safety and recovery protocol across both
adapters. The independent native interface therefore adds depth; generic provider/handler registries
would add interface cost. A slow source transaction deliberately blocks the complete selected feed.
This is measured technical publication order, not domain causality, FIFO delivery or external
exactly-once effects. Required inline command aggregates retain ADR0006's exact-head contract.

The [package contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore.Postgres/README.md)
owns supported setup and limits. Its [native consumers](../../src/Gloaming.EventSourcing/tests/Fixtures/EventFeed/README.md)
and [independent package adopter](../../tools/release/consumers/EventFeed.cs) own distinct proofs.
