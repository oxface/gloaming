# 0023: Explicit logical command receipts in native PostgreSQL transactions

Date: 2026-10-23. Status: accepted and implemented.
The [package contract](../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/README.md)
owns exact setup, supported model, failure and deployment obligations.

## Context

Several HTTP deliveries or operator requests can represent one logical action. Delivery IDs,
entity IDs and current workflow progress do not identify that action. An uncertain submission
or old compensation retry must recover its original accepted result without making a new decision.
A separately committed claim can strand unfinished work; a uniqueness check only at commit
cannot serialize the preceding business decision.

## Decision

Use an independently selected EF/PostgreSQL package with explicit Acquire/Retain operations.
A full scope/operation/GUID key takes a transaction advisory lock before comparing retained
input. Equal retained input exposes the original opaque versioned result; different input
conflicts. A new accepted decision stages one immutable receipt beside its business changes.
The consumer executes, saves and commits in its original ReadCommitted transaction.

The fixed six-column record, ordinal key/input meanings and documented V1 lock frame are
compatibility commitments. Native schema/table/column placement and migrations remain consumer
choices within that supported model. Future releases must preserve storage/coordination or
provide an explicit compatible upgrade; rolling writers cannot silently change lock framing.

Retain evidence for module lifetime without an automatic TTL. Explicit owner removal ends
duplicate protection. Consumers must retire admission before removing evidence when repeating
old actions would be forbidden. Result format/readers and sensitive-data policy belong to them.

## Consequences and limits

The library owns same-key serialization, comparison, immutable staging and tracked-save guards.
Consumers own authentication/authorization, logical identity, payload meaning, result readers,
context lifetime, native credentials, migrations, save/commit and failure reconciliation.
There is no handler runtime, chosen serializer, mandatory host or Messaging dependency.

Independent native, deployment and package-only adopters exercise this boundary.
Unknown result formats must never cause re-execution. This does not supply HTTP response replay,
safe pruning with permanent protection, cross-database atomicity or exactly-once external effects.
See the [package contract](../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/README.md).
