# Finite due-time consumers

These test-owned consumers adopt the public finite occurrence protocol, outbox handoff and
stale-generation rejection. Two small domain models keep
different relevance rules: probation reminders become obsolete, while review preparation uses
an approved-closure anchor. Their behavior is fixture policy, not a general workflow engine.

## Protocol and consumer boundaries

[NativeDueIntents](NativeDueIntents.cs) is test composition around the
[public PostgreSQL operations](../../../Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresDueIntents.cs),
which store a due occurrence with its original wire envelope.
Its key is admitted scope, source schedule and occurrence generation; outgoing delivery has a
separate stable ID. Stage, Suppress and bounded Release use the caller's fresh PostgreSQL
ReadCommitted transaction. Release selects at most 100 eligible rows in one scope using
FOR UPDATE SKIP LOCKED, marks them Released and enqueues their retained envelope fields through the
existing typed outbox. Caller save/commit makes those changes atomic. There is no claim lease
or external publication inside this transaction.
Native outbox JSONB preserves JSON values; lexical JSON bytes/property order are not replay guarantees.

Suppression holds the same native occurrence-row lock. Suppressed means a pending occurrence
was cancelled before release committed; AlreadyReleased means outbox handoff cannot be recalled.
Unchanged cancelled/missing outcomes are distinct. A result is a proposal until caller commit.
If release rolls back or its process dies before commit, another fresh caller can release the
original pending work. Retried release after an uncertain successful commit adds no second row.
SKIP LOCKED can temporarily omit busy work; callers poll again. No FIFO/fairness guarantee follows.

The caller supplies one explicit UTC cutoff at PostgreSQL microsecond precision. No wall clock,
calendar, recurring planner, default missed-occurrence policy or execution timeout is selected.
The tested consumer releases every persisted eligible one-off intent on a late call. A faulty
caller clock can release early in wall time; eligibility is defined against the supplied cutoff.
The native row's due time does not change merely because deployment code/configuration changes.
If the receiver's selected clock is still early for the current generation, it returns NotDue
without recording acceptance. Its adapter must retain/retry that delivery, not acknowledge it
as obsolete. The finite fixture reports exit 12; an inbox handler would use its explicit retry
policy. No consumer clock-skew or retry policy is installed automatically.

[Personnel](Personnel.cs) locks current business state before cancelling/replacing an occurrence
and before accepting a delivered reminder. Completion, cancellation, extension and reassignment
can invalidate its generation. The receiver checks current state, due time and generation,
then inserts one recipient-specific notification intent. Native source locking plus the
intent's complete unique key makes duplicate acceptance safe. Recipient selection is current
consumer policy, not timer payload authority.

An admitted reschedule can explicitly compose the existing command-receipt library.
ReplaceOnce retains its replacement generation/result atomically with source, suppression and
new intent; repeating its logical command after a lost commit response does not create another
generation. Ordinary Replace is a new decision on each invocation. Timer occurrence, delivery ID
and logical command ID have different lifetimes; a timer store must not guess their equality.

[Growth](Growth.cs) receives caller-computed UTC preparation dates. An approved closure, including
an early partial review, replaces the pending cycle. Cancelling an unfinished review preserves
the previous approved-closure anchor. Due acceptance creates a preparation indication; it never
initiates a review. No HR authority, actual review engine or time-zone rules are implemented.

Cancellation racing acceptance is serialized at the owning business row. If cancellation commits
first, the old occurrence is stale. If acceptance commits first, its notification intent remains.
Neither a native flag nor this fixture recalls an accepted/in-flight email. SMTP and any later
notification provider reconciliation are outside the proof.

## Native setup, retention and deployment

Each consumer owns its typed DbContext, business tables, due-intent table, command receipts and
provided outbox in a separate schema. Both native SaveChanges overrides validate the selected
receipt, due-intent and outbox capabilities. The provided occurrence model and handoff guards
belong to the library; source generation, relevance and notification policy remain here.

These process/race tests create empty disposable databases using native EF schema scripts.
The independent [package consumer](../../../../../tools/release/consumers/DueIntents.cs)
separately exercises a populated consumer migration and restricted module grants. Neither
certifies tenant RLS or two released scheduler library versions.
Scopes are explicitly constrained in every lookup; this is query-isolation evidence, not an
authorization system. Keep current source/generation decisions under the same native transaction.
Discard a failed/cancelled context and reconcile from fresh state.

The selected protocol retains pending, cancelled and released rows for module lifetime.
There is no expiry/pruner. Removing a pending row loses that wake-up; deleting retained rows,
reusing occurrence IDs or reconstructing history is an explicit owner action outside this
protocol. Receiver notification intents retain their occurrence uniqueness evidence.

Earlier/Current are separately compiled consumer binaries with V1-only and V1/V2 readers.
Release copies opaque payloads and can hand off a future body an old receiver cannot read.
An incompatible receiver fails without a business effect; a compatible replacement recovers
the same occurrence. New body writers require compatible receivers before delivery. This
does not select a general definition migration or payload upcaster.

The hosts use the parent outbox runner's ASP.NET shared framework because ChildHost launches
copied executable artifacts beside that runner. This is fixture composition, not a library
dependency or minimal-package proof. Environment connection values are redacted by ChildHost.
Finite roles accept an admitted scope and cutoff; test-only pauses expose staged/saved/committed
boundaries so the parent can kill its own children.

## Proof owners

- [Provider/clock/composition](../../OutboxPostgresTests/DueTimeIntentTests.cs)
- [Lock and cancellation races](../../OutboxPostgresTests/DueTimeIntentTests.Races.cs)
- [Different review-cycle policy](../../OutboxPostgresTests/DueTimeIntentTests.Growth.cs)
- [Actual process replacement and readers](../../OutboxPostgresTests/DueTimeIntentTests.Deployments.cs)

Run the existing outbox suite; its CI Messaging lane owns these tests:

~~~bash
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj
~~~

See the [finite library contract](../../../docs/due-intents.md) for complete setup, transaction,
clock, failure, lifetime and deployment obligations. Consumer correctness remains distinct
from the independent public-interface and package proofs.
