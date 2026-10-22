# Event reaction consumers

The executable adopts [Gloaming.EventReactions](../../README.md) and its
optional EF adapter without Wholesale dependencies. It demonstrates two consumer-owned models:

- DirectOrders inherits the consumer's own OwnedEntity and composes PendingEvents. Registration
  explicitly selects DirectOrder and DirectAllowance buffers.
- MappedQuotes returns ordinary immutable facts from a [package-free model project](Model).
  Application adapters map accepted state and raise facts through IEventSink<MappedQuotesModule>.

The model project references no Gloaming, EF or other package. Its records require no event marker.
Neither consumer inherits a supplied aggregate class or implements a source interface. The core also
accepts ordinary value records; tests exercise a scoped import request buffer and operation sink
without tracked event sources.

The host calls [AddDirectOrdersModule](Consumer/DirectOrdersRegistration.cs) and
[AddMappedQuotesModule](Consumer/MappedQuotesRegistration.cs), builds one provider and creates native
operation scopes. Contexts require only options. Typed reactions resolve lazily and use their scoped
context. The model declares ConfigureEventReactions; options install ConfigureGloamingCapabilities.

Two submission reactions reserve allowance and record a receipt. Reservation raises an allocation
event for a later wave; deletion releases allowance and removes related records. Every captured wave
preserves Raise order across selected buffers and the sink. Batches clear before handling and all
waves finish before validation and one native save. These bounded reactions share the caller transaction.

## Run and verify

Use a fresh disposable PostgreSQL database. Separate direct_orders/mapped_quotes migrations install
storage. Owner alpha has allowance 100. The journey submits a direct order of 40 and a quote of 60,
then deletes the order and releases 40.

```sh
DOMAIN_EVENTS_ASSESSMENT_CONNECTION_STRING='Host=localhost;Database=events_assessment;Username=postgres;Password=local' \
  dotnet run --project src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/EventReactionsDemo.csproj
dotnet test --project src/Gloaming.EventReactions/tests/EventReactionsTests/EventReactionsTests.csproj
dotnet test --project src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/Consumer.Tests.csproj
```

Tests use PostgreSQL 18.6/Testcontainers; see [development setup](../../../../docs/development.md). They
also compose real tenant validation and an outbox to prove preparation/validation order. The executable
itself references only EventReactions, shared EF infrastructure, its own model, Npgsql and native DI.

## Saving and recovery

Begin the owning transaction, execute commands, save natively and commit after saving returns.
Both sync and async paths exist; sync rejects async-only reactions. Source registration is explicit:
an omitted buffer is not discovered or drained. Include current sources before dispatch and capture
deletion facts before detachment. The default ceiling is 128 reaction invocations per dispatch.

After SaveChanges(false), use native AcceptAllChanges before continuing work. Events are consumed
independently of tracker acceptance. Negative proofs cover premature acceptance, SQL bypass and
facts introduced after preparation. Any failure requires rollback/disposal and a fresh business decision.
Consumed events cannot repair a later rollback. There is no Stage, owned transaction or acknowledgment.

Use explicit orchestration for complex dependencies. Durability, serialization, integration mapping,
external effects and direct-contract saga recovery are separate capabilities. See the
[lifecycle contract](../../docs/local-dispatch.md) and
[current report](../../../../docs/reports/event-reactions-consolidation.md).
