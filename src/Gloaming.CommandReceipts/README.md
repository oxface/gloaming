# Gloaming.CommandReceipts

Explicit logical-command receipts, independently selectable from Messaging, actor context,
tenancy and application business models. The consumer owns admission, identity, codecs and
native infrastructure.

The [EF/PostgreSQL package](Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/README.md)
documents the complete contract, retention, deployment and transaction obligations.
[Independent provider/deployment proofs](tests/CommandReceiptTests/CommandReceiptTests.csproj)
use a separate context and separately compiled consumers; [Sales](../../samples/Wholesale/modules/Sales/command-receipts.md)
demonstrates composition with real permissions, required reactions and workflow outbox requests.
