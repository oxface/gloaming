# Retired source and plan provenance

Use Git history for retired source, dated execution results and superseded proposals.
Current implementation contracts live beside each library; the
[library audit](gloaming-library-audit.md#remaining-cleanup-slices) is the active work queue.
Historical instructions and approvals apply only to their original work.

## Original sample archive

The original source revision is
[`15d1ec6ce85f872e4547b37c33658dea76b25d03`](https://github.com/oxface/modulith-foundry/tree/15d1ec6ce85f872e4547b37c33658dea76b25d03).
It precedes the archive baseline
[`790a8b76b7a519039f61434f330d2ed1b07d70e9`](https://github.com/oxface/modulith-foundry/tree/790a8b76b7a519039f61434f330d2ed1b07d70e9),
which retains the [archive guide](https://github.com/oxface/modulith-foundry/blob/790a8b76b7a519039f61434f330d2ed1b07d70e9/archive/README.md)
and [800-file manifest](https://github.com/oxface/modulith-foundry/blob/790a8b76b7a519039f61434f330d2ed1b07d70e9/archive/proof-sample/SNAPSHOT.json).

Checkpoint `836e4ed` retired the 802 archive paths and the obsolete checksum verifier after
transferring active proof obligations. The
[original retirement report](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/reports/s1-archive-retirement.md)
records its exact scope, checks, historical counts and then-pending integration work. Those
results are dated evidence, not fresh verification of the current smaller application.
No replacement archive, frozen source copy or reconstruction patch is maintained.

The two originally retained Inventory event literals remain owned by
[Events fixtures](../../src/Gloaming.Events/samples/EventCodecDemo/Fixtures/README.md):

| Fixture | Original SHA-256 |
| --- | --- |
| `Inventory/opened.v1.json` | `a5f6294f14afcee9b10f825be292d2ddd018834d3c7c8402a48671830d99d2d2` |
| `Inventory/received.v1.json` | `feece448f1623f9a350995e714cee156d22ca22fbd06d3b80cae8835266c433a` |

Their original hashes do not certify every later fixture; each owning fixture guide records
its independent origin and compatibility purpose.

## Library-first consolidation

The last complete tree before plan consolidation is
[`0d43d3cf746bc5b70dec60b8864459cf97c7d65d`](https://github.com/oxface/modulith-foundry/tree/0d43d3cf746bc5b70dec60b8864459cf97c7d65d).
It contains the [80 retired dated plans](https://github.com/oxface/modulith-foundry/tree/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans),
including the old extraction sequence, remaining-capability roadmap and integration ledger.
Retained historical references point to verified blobs at this immutable revision. The
current [extraction gate](../plans/library-extraction.md) replaces the execution sequence;
[design](../design.md) replaces its accumulated cross-family implementation diary.

The generator and ContextDemo retired at `dd6eb77`; family example consolidation at
`ae2909c`; Wholesale Purchasing at `8601204`; Dispatch and Sales directory policy at `0d43d3c`.
The [audit implementation record](gloaming-library-audit.md#implementation-progress) maps
those changes to fresh proof results and explicit retirements. Detailed dated reports still
await the next relevance pass; their old status text does not reopen completed work or grant
commit authority.

## Current proof ownership

| Obligation | Current owner |
| --- | --- |
| Payload identities, JSON evolution and historical literals | Events family fixtures/tests; Wholesale event-persistence tests. |
| Module migrations, ownership, native transactions and RLS | Persistence/Tenancy family proofs; retained Wholesale module migrations, API migration/SQL-fixture and isolation tests. |
| Append, required inline aggregate, history and rebuilding | EventSourcing family tests; Wholesale Inventory and Worker maintenance proofs. |
| Inbox/outbox atomicity, retry, leases, retention and native recovery | Messaging family contracts/provider tests, test-owned process/deployment fixtures and Wholesale reservation/workflow tests. |
| Context establishment, Access admission, business permissions and accepted audit | Independent context/HTTP/audit family tests, native context-composition tests and retained Wholesale integration/runtime proofs. |
| Library dependency isolation and ordinary package adoption | Architecture tests and the [six-profile package verifier](../../tools/release/README.md#full-package-adoption-proof). |
| Generator creation, retired directory/business journeys | Retired consumer/tooling behavior. Dated evidence remains in Git; no maintained guarantee is inferred. |

Preserve owned active fixtures and migrations when moving or pruning code. A sample may retire
its own invented policy; a supported library failure guarantee needs retained or replacement
proof. Library mechanisms, editable application setup and sample rules remain separate.
**No new reusable Gloaming mechanism is established by retiring history.** Final integrated
verification, independent code/architecture review and the owner-selected extra documentation/
test pass remain open in the audit queue.
