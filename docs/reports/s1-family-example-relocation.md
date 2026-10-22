# S1: independent family example relocation

This increment moves the EventReactions, EventSourcing storage and Messaging examples/tests
beside their owning family. Namespaces, assembly identities, migrations, fixture bytes and
protocol assertions remain unchanged. Native project/fixture references, the solution,
architecture declarations, CI paths/commands and documentation links follow the new paths.

Messaging's independent producer/worker no longer reference Wholesale.ServiceDefaults. Their
small native host/OTel setup is consumer-owned under the Messaging examples, not another
Gloaming runtime package. The examples retain separate inbox-only/outbox-only/combined and
process-host adoption obligations; these are not interchangeable tests to delete by count.

The initial script/build attempt hit sandbox child-process restrictions before relocation;
verification then ran with normal native IPC. A superseded build was stopped rather than
counted. The confirming active-solution build completed with zero warnings/errors. Current
PostgreSQL/RabbitMQ/example/architecture suites all pass; exact results are in
`/tmp/v1-family-move-tests.log`, and the clean build in `/tmp/v1-family-build-confirm.log`.
The standalone process suite still exercises broker recovery, SIGTERM, duplicate acceptance,
retention, schema evolution and native OTLP boundaries after relocation.

This is the first S1 increment, not whole-product consolidation or release completion.
Wholesale's slice hosts, remaining technical examples, template selections and archive
retirement remain tracked in the integration ledger. No new reusable mechanism was proven.
The root two-axis review of this fixed increment is recorded before final closure.
