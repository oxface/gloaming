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

## Technical-example continuation

ContextDemo, PersistenceDemo and EventCodecDemo (and their test projects) now belong to
Tenancy, Persistence and Events respectively. They were technical-only toy consumers rather
than the module implementations, so their relocation removes six more slice directories from
Wholesale without changing its business modules. Existing namespaces, fixture literals and
migration identities remain intact. The ownership console proof exposed a brittle fixed-depth
repository lookup; it now launches the built executable assembly directly, matching the actual
process boundary and remaining independent of folder depth. Its first failed relocation run
is diagnosis evidence, not counted as a pass.

The confirming active solution builds with zero warnings/errors. The final context, ownership,
codec and architecture suite results are `/tmp/v1-technical-move-tests-final.log`; all pass.
CSharpier checks the solution/new sample defaults. Native project links and all active commands
follow the family-local paths. No technical-library runtime interface changed.
