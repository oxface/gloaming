# Native feed consumers

`FeedConsumer` owns ordinary ledger and tenant-qualified review journals plus generation-qualified
output tables. Its ledger reducer sums signed amounts; its review reducer counts named facts with
complete case-sensitive ownership keys. Both call only the public feed module. Migrations and
business readers remain consumer-owned. This is disposable library proof, not an Umbrine app.

`EventFeed.Earlier` is separately compiled with Npgsql only: its unchanged INSERT statements run
before and after feed installation. The native migration waits for its in-flight transaction;
subsequent inserts are captured without application feed registration. Its process barriers prove
head/capture rollback, committed recovery and cancellation of a genuinely blocked writer.

`EventFeed.Proof` pauses its public projection operation at staged, saved and committed phases;
killing that owned child and running a fresh turn verifies output and checkpoint recovery together.
These hosts require an explicitly supplied disposable `FEED_CONNECTION` and execute no startup DDL.

The owning tests are [FeedTests](../../EventSourcingPostgresTests/FeedTests.cs) and
[FeedRecoveryTests](../../EventSourcingPostgresTests/FeedRecoveryTests.cs). The
[public contract](../../../Gloaming.EventSourcing.EntityFrameworkCore.Postgres/README.md) owns limits.
