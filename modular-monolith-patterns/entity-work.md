# Serializing work for one entity

Different deliveries can target the same consumer entity. Choose an explicit complete entity key
when cooperating workers must exclude each other across those deliveries. Native PostgreSQL
transaction advisory locks are sufficient for this selected composition; no additional Gloaming
library interface or reusable mechanism is introduced.

Use the owning module's fresh native ReadCommitted transaction. Acquire the lock **before** reading the business
state used for the decision. Hold it through the selected state/outbox save and native commit.
Rollback, connection loss and process death release a transaction lock. Committing releases it
even while the process/connection remains alive. Do not substitute a session advisory lock on a
pooled connection, release early or move the business decision to another transaction.
See PostgreSQL's [advisory-lock lifetime](https://www.postgresql.org/docs/18/explicit-locking.html#ADVISORY-LOCKS)
and [native advisory functions](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS).
Rolling back to a savepoint created **before** acquisition releases that lock even while the
outer transaction remains open. Discard the old decision; reacquire and reread in a supported
fresh decision before doing more protected work. A savepoint is not evidence of continued exclusion.

## Native composition

All cooperating deployed writers agree on the same versioned key protocol and namespace. This
example uses UTF-8 byte lengths to frame opaque module, technical scope and complete entity keys.
Native ID values must be canonically encoded by their consumer; an entity with tenant/partition
components includes all of them. Module and scope are not inferred from optional message metadata.
Key comparison is ordinal and case-sensitive. A delimiter-joined ambiguous string, `GetHashCode`
or a delivery ID is not the same protocol.

```csharp
string[] fields = ["consumer.entity-work.v1", moduleKey, scopeKey, entityKey];
foreach (string field in fields)
    ArgumentException.ThrowIfNullOrWhiteSpace(field);

string framed = string.Concat(fields.Select(field =>
    FormattableString.Invariant($"{Encoding.UTF8.GetByteCount(field)}:{field}")));
long lockKey = BinaryPrimitives.ReadInt64BigEndian(
    SHA256.HashData(Encoding.UTF8.GetBytes(framed)));

await using var command = new NpgsqlCommand(
    "SELECT pg_advisory_xact_lock(@key)", connection, transaction);
command.Parameters.AddWithValue("key", lockKey);
await command.ExecuteNonQueryAsync(token);

// Now load and decide using this transaction. Save selected effects/outbox, then commit.
```

Imports are `System.Buffers.Binary`, `System.Security.Cryptography`, `System.Text` and `Npgsql`.
The application owns `connection`, `transaction`, key admission, bounded execution and cleanup.
EF consumers may use the current Npgsql connection/transaction from their owning context; don't
open a separate connection for the lock. Database names are naturally separate PostgreSQL lock
namespaces. The prefix separates this protocol from logical command receipts and other advisory
lock conventions. A 64-bit hash collision adds unnecessary contention; it does not admit two
equal complete keys simultaneously. Advisory locking remains cooperative: raw SQL or other
writers that omit it can still change rows. Keep native concurrency predicates/constraints.

## Guarantees and limits

This supplies mutual exclusion among cooperating transactions for the same complete key. It
does **not** supply FIFO, ordering by delivery or commit/start time, fairness, deduplication,
retry policy, admission or exactly-once effects. A waiter may be cancelled or selected in a
different order. Consumers control queue order and retry/backoff if their business needs them.
Use logical command/inbox receipts separately for retained duplicate/content evidence. Recheck
current business state and permissions after acquisition; the lock grants no authority.

Use one complete entity per transaction where possible. If a decision locks several entities,
every writer must use the same total acquisition order; otherwise native deadlock detection may
abort one transaction. Keep work bounded, propagate cancellation and dispose failed transactions.
Retry only a fresh authorized decision according to consumer policy. A lost commit response may
already mean success; inspect retained business/receipt evidence, not the existence of a lock.
No generic lock table, automatic worker serialization or cleanup scheduler is installed.

Ordinary module credentials need only their selected business-table/schema permissions and
EXECUTE on the native advisory-lock function. The usual PostgreSQL public function grant suffices
in the proof; deployments that revoke it must deliberately grant required execution. No migration,
retention, superuser or BYPASSRLS authority is needed. Business tables/migrations and reader/writer
deployment remain application-owned. Changing key encoding/namespaces during mixed deployment
requires a coordinated writer rollout; old and new lock keys would not exclude each other.

## Executable evidence

The [native-only worker fixture](../src/Gloaming.Messaging/tests/Fixtures/EntityWork/README.md)
references Npgsql without Gloaming packages. Two real worker processes prove same-key exclusion
with an observed ungranted native advisory lock; different module/scope/entity keys proceed while
the first transaction stays open. Other cases cover commit while the process stays alive,
rollback, killed process recovery, cancellation of an actually blocked acquire, fresh recovery
and restricted ordinary runtime grants with denied retention deletion. Literal full-key hash
vectors retain the native composition protocol. These tests establish this PostgreSQL/Linux
composition, not arbitrary providers or host topologies.

Run the owning provider suite:

```bash
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj
```
