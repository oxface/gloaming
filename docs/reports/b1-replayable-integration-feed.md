# B1: discarded retained-feed experiment — dated results

Experiment executions: 2026-10-13. Final disposition: documentation-only assessment closed
on 2026-10-14; the owner subsequently requested removal of all experiment code and patch
artifacts. This report retains results, assumptions and limitations. There is no maintained
feed implementation, runnable proof suite or reconstruction artifact in the current tree.

## Protocol investigated

The experiment selected accepted Sales profile edits and stored complete public after-images
in the same native transaction as customer/address/audit changes. It was a retained pull feed,
not CDC, domain-event publication or a broker integration.

One feed-state row per Organization held generation, committed head H and retention floor F.
Writers acquired its lock before loading business state, allocated a PostgreSQL sequence
position only after acquiring the lock, and held it through commit/rollback. This prevented
a later position in that feed from committing ahead of an unresolved earlier allocation.
Rollback and other-Organization allocations could leave legitimate gaps.

Each bounded page used one short read-only RepeatableRead snapshot of metadata and entries.
Truncated pages advanced through the last returned entry; exhausted pages advanced to their
captured H. Retention deleted a prefix and advanced F atomically. Cursor C=F remained valid,
C<F expired, and wrong generation/future positions were rejected. Expiry stopped the reader
without silently resetting progress.

A separate PostgreSQL diagnostic journal committed receipts and checkpoint together.
Repeated pages had no second diagnostic effect; changed duplicate content failed, competing
checkpoint writes used native version predicates, and stale pages could neither skip an
unprocessed suffix nor regress progress. This journal was not a business projection.

## Historical verification

These executions used Testcontainers PostgreSQL 18.6, disposable databases, rootless Podman
and native connections. Child-process tests killed only their own reader processes.
Lock/query/transaction barriers established ordering; sleeps alone were not the proof.

| Check on 2026-10-13 | Result |
| --- | --- |
| Whole active solution build | Passed, zero warnings/errors. |
| Experimental feed suite | 23 passed, zero failed/skipped. |
| HTTP regression suite | 105 passed, zero failed/skipped. |
| Architecture suite, including experimental host boundary | 83 passed, zero failed/skipped. |
| Whole-solution style/analyzer verification | Passed. |

The experimental proofs covered:

- A counterexample to naive sequence/MAX paging: higher B committed before lower A;
  advancing to B then committing A lost A from subsequent suffix reads.
- A blocked late commit and a transaction that started earlier but acquired the gate later:
  same-Organization writers remained correctly visible behind committed progress.
- Rollback/other-Organization gaps, bounded/empty pages, writes between pages, cursor-in-gap
  validity, captured-head bounds and tenant isolation.
- Retained entry/head, address/audit and deferred commit failures, plus cancellation after
  initial save/allocation: all participating writes rolled back together.
- Retention through positions/gaps, boundary/expiry, invalid generation/future cursors,
  idempotent/older cutoffs, all-rows-pruned pages and atomic deletion/floor rollback.
- A reader paused between metadata and entries during pruning: its old snapshot completed
  consistently, and its next read observed expiration.
- Duplicate pages, stale producer retries, conflicting content, failed receiver commit,
  competing readers and out-of-order stale-page completion.
- Reader death before save, after save/before commit and immediately after commit:
  receipts/checkpoint rolled back or survived together, and restart resumed from saved progress.
  An expired reader stopped without replacing its checkpoint.
- A forward migration over populated Sales storage preserved profiles, audit, a WF2 process,
  outgoing request and migration history. Activation invented no past profile changes.

After removing the feed from the common sample on 2026-10-14, fresh checks passed:
active solution build with zero warnings/errors, 105 HTTP tests, 36 persistence/migration
tests and 82 architecture tests, all with zero failures/skips. Style/analyzers and CSharpier
also passed. Those checks verified restored common-sample behavior, not the stored experiment.

The experimental source and tests have now been deleted. These counts are dated execution
evidence; they cannot be reproduced by running a feed suite in the current tree.

## Findings and rejection

Technical correctness was demonstrated for the selected protocol under its writer-participation
assumptions. Business adoption value was not established. No real receiver requirement
justified serializing ordinary Sales profile edits by Organization.

The writer gate imposed throughput/write coupling that was not benchmarked. Old writers
and native SQL bypasses could break completeness. Arbitrary deletion above F was
indistinguishable from legitimate sequence gaps. No initial population, deletion export,
snapshot/live cutover, subscriber pinning, automatic expiry recovery, payload upgrade,
multi-source order or external exactly-once effect guarantee was proven.

Public disclosure, admission, source participation and retention were producer policies.
Subscription identity, duplicate-content decisions, atomic receiver progress and readiness
were receiver policies. The diagnostic receiver did not establish a useful serving model
or separately deployed service composition.

The owner rejected retaining this implementation as sample or experiment code.
Only the [assessment](b1-retention-and-contract-assessment.md), research and this result
record remain. Common Wholesale has no feed API, writer gate, model mapping, activation or
experimental migration. Existing archive and common migration files are unchanged.

No new reusable mechanism was proven; no library interface or template preset was extracted.
Any later replay/state-transfer implementation needs a concrete receiver requirement and
its own reviewed protocol and fresh failure proofs.
