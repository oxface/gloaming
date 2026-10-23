# Immutable-model reaction example

One package-free quote model and one native EF consumer demonstrate explicitly staged local
facts and an ordered bounded reaction cascade. Domain code needs no Gloaming marker interface
or base class. The consumer selects its scoped sink and handlers, owns native saving and
commits the quote, allowance, receipt and allocation together.

```bash
export DOMAIN_EVENTS_ASSESSMENT_CONNECTION_STRING='<fresh disposable PostgreSQL connection string>'
dotnet run --project src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/EventReactionsDemo.csproj
```

[ReactionExample](Consumer/ReactionExample.cs) explicitly migrates/seeds the mapped module,
accepts one quote and saves/commits its local effects. The expected result is `reserved=60,
receipt=1, allocation=1`. This finite fixture requires fresh storage; it is not an application
bootstrap service. The allowance ceiling and quote rules are consumer policy.

The [model](Model/MappedQuote.cs) has no project, package or framework references.
[MappedQuoteCommands](Consumer/MappedQuoteCommands.cs) uses an explicitly selected operation
sink; [registration](Consumer/MappedQuotesRegistration.cs) declares the reactions and installs
native EF capability composition. Saving is the caller's explicit consistency boundary.

The [adoption suite](../../tests/AdoptionTests/Consumer.Tests.csproj) owns the alternate
[direct-source context](../../tests/AdoptionTests/Fixtures/Direct/DirectOrders.cs), its
consumer-owned base, registration and migrations. Both source shapes retain native save,
staging, deletion, ordering, bounded-cascade, cancellation and failed-save recovery proofs.
Actual shared EF preparation/validation composition is tested with ownership and outbox roles.
The old prototype EF research probes are retired; current tests exercise Gloaming directly.

```bash
dotnet test --project src/Gloaming.EventReactions/tests/AdoptionTests/Consumer.Tests.csproj
```

The suite also launches the actual focused executable against PostgreSQL. The public consumer
references neither the test fixtures nor Persistence/Messaging; those are selected only by
cross-capability adoption tests. See [current guarantees and limits](../../docs/local-dispatch.md)
for supported collection, acknowledgement, transaction and retry obligations.
