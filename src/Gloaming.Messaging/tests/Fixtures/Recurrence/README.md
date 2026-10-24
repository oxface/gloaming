# Recurrence native fixture

Test-owned executable host for PostgreSQL recurrence process recovery and pinned time-zone data
replacement. `OutboxPostgresTests/RecurrenceTests` launches only its own child and kills it after
staging, native save or commit. Fresh planning observes the durable result. The same host loads
an explicitly isolated TZDIR dataset in separate processes; replacing actual UTC TZif bytes with
offset-one-hour bytes refuses planning without changing progress or retained UTC occurrences.
No library code changes process-global time-zone configuration.

The provider suite also executes elapsed UTC maintenance and explicitly zoned recurring indication
payload adapters through spring gaps and both interval autumn offsets. Their payloads, recipients,
anchors and schedule relevance are consumer choices. Native concurrency, cancellation, rollback,
savepoint, omitted-participant, lost-acknowledgement and bounded stop tests use the public module.

This is an initial provided protocol proof, not a claim of two released recurrence library versions
or an Umbrine application. The independent package adopter owns versioned migrations and restricted
runtime grants. Run:

```bash
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj
```
