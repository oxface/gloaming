# Native entity-work fixture

An ordinary Npgsql executable with no Gloaming library reference. It composes transaction advisory
locking with a native consumer counter to prove exclusion before decision reads. All processes,
databases, roles and signal/release-file coordination are disposable and test-owned. Importing and
billing workers use complete module/scope/entity identity; application key admission and payload
policy are not a library feature.

[EntitySerializationTests](../../OutboxPostgresTests/EntitySerializationTests.cs) launches separate
workers and observes actual ungranted PostgreSQL advisory locks, independently expected effects,
different-key progress, rollback, cancellation, process death and commit-before-process-exit.
The restricted-role case audits grants and proves denied deletion. This adds no reusable Gloaming
mechanism; it validates the [native application recipe](../../../../../modular-monolith-patterns/entity-work.md).
No FIFO, fairness, admission, retained deduplication or external exactly-once behavior is claimed.
