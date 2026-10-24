# Sales profile proof setup

[DddComposition](DddComposition.cs) establishes explicit tenant/actor scopes and native Sales
services for the [domain decisions](../../Domain/DomainDecisionTests.cs) and
[persistence failures](../../Domain/ConsumerTests.cs). Immutable profile details and accepted
candidates remain Sales policy. The module service owns its native save/commit and accepted-change
audit; attribution alone grants no authority.

Inventory exercises the event-sourced
candidate/append path in [EventPersistence](../EventPersistence/README.md); the API's
[profile tests](../../Api/ProfileTests.cs) exercise actual Sales consumer behavior.

Run through the [Wholesale integration project](../../README.md).
