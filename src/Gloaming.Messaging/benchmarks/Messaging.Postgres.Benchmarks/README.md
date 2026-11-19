# PostgreSQL delivery benchmarks

From the repository root, run `npm run perf:messaging`.
From `src/Gloaming.Messaging`, use `npm run perf`. Both require the pinned .NET
SDK and a Docker-compatible container engine.

BenchmarkDotNet measures the time to drain a batch of 512 messages through real
PostgreSQL outbox dispatch, transactional inbox intake and durable handler
effects. A local publisher passes the envelope between the outbox and inbox.
The cases compare one and four workers with 32 or 1,024 previously completed
messages. Each iteration resets and seeds its database before measurement,
then verifies committed message identities and values after measurement.
The fixture checks that PostgreSQL runs with `fsync` and `synchronous_commit`
enabled.

Native reports are under `messaging-postgres/results/` in the printed run directory.
The unit of measurement is one batch. Divide 512 by the mean batch time in
seconds for messages per second. Compare repeated runs on the same machine
with the same container configuration.
