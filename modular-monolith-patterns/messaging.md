# Add durable handoff and logical command receipts

Select this document when the requested behavior needs work to survive process loss,
independent module commits or ambiguous command completion. Modules alone do not imply messaging.

## Define the boundary

Name the sender, receiver, wire contract, admitted scope, logical action and failure behavior.
Decide whether delivery is a native local adapter or an external transport.
The application owns routes, queues/subscriptions, credentials, identity admission and workers.

Keep these identities distinct:

| Identity | Meaning |
| --- | --- |
| Entity/aggregate ID | Business resource |
| Logical command ID | One accepted action, reused after an uncertain response |
| Delivery MessageId | One retained outgoing/incoming envelope |
| Correlation/causation | Conversation and immediate cause |

## Outbox and inbox

The sender stages an immutable outgoing envelope with its business changes in the same
module context/transaction, then saves and commits. Install the required native outbox guards.

A separately invoked dispatcher publishes retained messages outside the claim transaction.
Transport acceptance can be uncertain; delivery can repeat. Database fencing cannot recall an
external provider effect.

The receiver validates transport/wire/scope admission, durably retains intake and commits before
acknowledging transport. Its independently invoked inbox processor commits local effects,
completion and any next outgoing message together. The selected processor owns that bounded
transaction; follow its savepoint, failure-accounting and cancellation contract.

Use the callable storage/dispatch/processing roles directly or select the separate Hosting
package. The application decides polling, supervision and process restart.
Broker-free adapters must still preserve retained intent, stable identity, admitted intake and
recoverable response delivery. A direct independently committing method call alone does not.

Done: native tests prove producer rollback, receiver deduplication, redelivery after uncertain
acceptance, competing processing and recovery after process death.

## Logical command receipts

Use receipts for an explicitly selected action whose accepted result must be recovered after a
lost response. The client/operator retains the action ID before sending and reuses it for that
same intent. A deliberate new action uses a new ID.

Authorize before exposing a retained result. In one fresh ReadCommitted context/transaction,
acquire the full scope/operation/action key and compare stable consumer-selected input.
An existing result is decoded with a compatible reader; a new accepted decision retains its
result/reference with the business and outgoing changes before save/commit.

The consumer owns comparison meaning, result format and readers, permissions, retention and
migration. Retain evidence for module lifetime under the current contract; explicit removal ends
duplicate protection. Unknown result formats must fail without re-executing the action.

Done: equal concurrent attempts commit one effect, changed input conflicts, rollback leaves
no completed receipt, and a fresh authorized retry recovers an uncertain committed result.

## Longer processes

Select the optional continuation runner only for its supported saved-method/request-reply model.
Business branching, compensation, response validation and operator authority stay in the module.
One pending request is supported; parallel joins, human-workflow assignment and scheduler/deadline
semantics require their own design. Keep unsupported features explicit.

A due-time signal is an invitation to reconsider current business state, not authority to
complete a task. Generation/current-state checks remain necessary after cancellation or reassignment.
Gloaming currently supplies no public scheduling API.
