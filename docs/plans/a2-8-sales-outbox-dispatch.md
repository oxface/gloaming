# A2.8: restricted Sales outbox dispatch

Owner authorized on 2026-10-17 after checkpoint `192fe7d`, on the existing
`feat/sales-postgres-rls` branch. Implement one consumer composition using the existing
PostgreSQL dispatcher, worker and native RabbitMQ publisher. No Gloaming interface changes.

The explicit `setup-sales-dispatch` executable role provisions a new login against an already
migrated Sales outbox. The separate `dispatch-sales` role receives only its own runtime and
broker connections. A small context maps the existing outbox table, without business mappings
or an operation tenant accessor. No migrations, business operations or inbox handlers run here.

Dispatch intentionally reads queued payloads across tenants. Its role may SELECT that table
and UPDATE only lease token/expiry, attempts, availability and completion. It may not change
the envelope, INSERT/DELETE/TRUNCATE, administer schema/roles or access other application
relations. Business tables keep their existing RLS; dispatch receives no BYPASSRLS, owner
membership or alternate business credential. The outbox itself retains its existing global
delivery policy; this is privilege separation rather than tenant filtering of the delivery queue.

Required proofs use real PostgreSQL/RabbitMQ: both-tenant delivery; direct database rejection
of business access and envelope edits; actual login/catalog checks including column grants;
failed publication and post-publication completion failure/retry; competing workers with
separate scopes; startup without provisioning, schema creation or business credentials.
Preserve current claim/publication/completion transactions and at-least-once delivery.

Inbox tenant establishment/atomic effects, default workflow rollout, production provisioning/
rotation, broker ACL hardening, automatic policy scaffolding and template changes remain
separate. New code and documentation stay unstaged for owner review.
