# Contribution native consumers

Test-owned import and offer consumers adopt the public technical contribution protocol in separate
schemas. Importing selects all three chunk results and computes its own final row count. Offers
selects two responses from three vendors, then independently applies its own budget rule; technical
completion can become business rejection. Completion/timeout notices do not themselves update
business state. Current-generation checks, source-row locks, readers and duplicate outcome handling
belong to these consumers. No Umbrine application or human workflow model is implemented.

The executable host lets the provider suite kill only its own process after staging, SQL save or
commit. Fresh retries recover retained contribution/notice identity and terminal state. Native
contract tests separately exercise duplicate equal/changed content, both timeout/last-arrival lock
winners, emitted-signal relevance, original/current/stored participant guards, rollback, response
loss, JSONB boundaries and native save forms. Undefined persisted business enum states reject at
the consumer read boundary. Payload/participant/threshold policies do not move into the library.

Run `OutboxPostgresTests` from the root. The [public contract](../../../docs/contributions.md)
defines complete native mapping/transaction/authority/retention obligations. The package-only
adopter owns its own literal migration and grant proof; this fixture is not evidence of two
released contribution library versions.
