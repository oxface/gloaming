# E9 audit context and envelope research

Research date: 2026-10-09. Primary-source review supporting the owner's feedback on the
unstaged E9 implementation. Recommendations below are design proposals, not new executable
proofs or approved public interfaces at that research checkpoint. The subsequent owner
decision removes correlation, causation and trace IDs from audit under YAGNI;
[the implementation report](e9-explicit-transactional-audit.md) records the current capability.
The tracing/audit-context suggestions below are historical proposals, not supported audit
fields. Native tracing facts and EF/JSON equality findings remain applicable. No archived
source was changed.

## Native request tracing

`System.Diagnostics.Activity.Current` is the native ambient tracing context. It flows across
asynchronous calls. W3C identifiers are the default for new traces in .NET 5 and later:
`TraceId` identifies the trace, `SpanId` identifies the current operation, and `ParentSpanId`
identifies its parent span. ASP.NET Core and `HttpClient` understand HTTP trace propagation
without a custom Gloaming context. [Microsoft distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts),
[Activity.Current API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity.current?view=net-10.0).

Tracing does not supply application actor or tenant identity. The W3C specification permits
trace restarts at trust boundaries, and its identifiers describe tracing operations.
Consequently, a span ID is not evidence of which durable command message caused a business
change. This distinction is an inference from the specified meanings and restart behavior.
[W3C Trace Context processing and mutation](https://www.w3.org/TR/trace-context/#mutating-the-traceparent-field).

Historical proposal (superseded for audit): capture a nonzero W3C `Activity.Current.TraceId` when present for HTTP
interaction correlation. Capture it once per audit operation and allow absence in direct
module calls. Do not synthesize a trace with unrelated identity merely to fill a field.
Whether HTTP trace IDs are used as the sample's `CorrelationId` is an explicit consumer
convention; a separately named trace field is preferable if business conversation IDs and
trace IDs must coexist in one entry.

Inventory already has the appropriate durable source in
[InventoryMessageContext](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/InventoryMessageContext.cs):
its incoming envelope contains `CorrelationId` and `MessageId`.
[StockIssueMessages](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessages.cs)
already copies conversation correlation and identifies the incoming message as the outgoing
message's cause. An audit wrapper can reuse the same admitted metadata. Preserve this cause
through retries; do not replace it with the worker's current span ID. Actor and tenancy
accessors remain the established sources for attribution and ownership.

## Records and JSON equality

Microsoft recommends avoiding value equality on EF entity types. EF itself compares tracked
entity instances by reference even when equality is overridden; collection navigations can
be affected by overridden equality. A record is therefore not automatically broken in EF,
but introduces inappropriate entity equality and does not solve this staging guard.
[EF Core identity resolution](https://learn.microsoft.com/en-us/ef/core/change-tracking/identity-resolution#overriding-object-equality),
[C# record guidance](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records).

Synthesized record equality delegates to each member's equality. It does not recursively
compare arbitrary JSON. `JsonElement` is a struct containing a backing document reference
and index, without a content-based `Equals` override in the inspected .NET 10 source;
inferring JSON content equality from its default struct equality is unsafe.
`GetRawText()` returns the original JSON text, so string comparison detects differences in
formatting and property order. `JsonElement.DeepEquals` is available in .NET 10 and compares
content: object property order is generally irrelevant, equivalent decimal number
representations compare equal, array order matters, and duplicate properties retain ordering
requirements. [Pinned .NET 10 JsonElement source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonElement.cs),
[DeepEquals API](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.deepequals?view=net-10.0).

Keep `AuditRecord` as an EF entity class. An internal immutable snapshot record containing
scalar envelope values and the raw JSON string could reduce manual comparisons while
preserving the existing exact-text guard. It would still require deliberate field capture.
Changing to `DeepEquals` is a separate choice to weaken text identity to JSON value equality;
it should not occur as an incidental record refactor. The existing outbox pending-envelope
guard also uses exact `GetRawText()` comparison in
[OutboxModelExtensions](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxModelExtensions.cs).
Any shared helper should be justified by actual repeated mechanics and reviewed independently
of unrelated messaging behavior.

## Audit guidance and scope

OWASP recommends recording sufficient information about event time, location/source, actor
and event meaning, with action, affected object, result, reason and interaction identifiers
where appropriate. Its field examples vary by application; it encourages summaries when full
content is unnecessary. It specifically recommends excluding or protecting tokens, passwords,
secrets and sensitive personal data. These are application logging recommendations, not a
mandatory universal audit-envelope schema.
[OWASP Logging Cheat Sheet: attributes and excluded data](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html#event-attributes).

NIST SP 800-53 Rev. 5 AU-3 covers event type, time, location, source, outcome and associated
identities. AU-3(3) limits personal information according to the organization's assessment;
AU-8 specifies time stamps with UTC or an explicit offset; AU-9 addresses protection against
unauthorized access/change/deletion; AU-11 leaves retention duration to organizational policy.
These controls describe outcomes rather than a C# field list.
[NIST SP 800-53 Rev. 5, AU controls](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-53r5.pdf).
The current NIST publication page links Release 5.2.0. Its change summary adds SA-15(13),
Logging Syntax, and related-control links from AU-3; it does not list changed requirements
for the AU controls above. This release reinforces deliberate logging conventions rather
than supplying an application audit-row schema.
[NIST publication and release links](https://csrc.nist.gov/pubs/sp/800/53/r5/upd1/final),
[Release 5.2.0 change summary](https://csrc.nist.gov/files/projects/Risk-Management/800-53%20Comment%20Site/SP800-53-r5.2.0-changes.pdf).

For E9, the inference is to retain meaningful action, subject classification, attribution,
time, source and accepted outcome while removing repetitive caller setup. A sample wrapper
can obtain actor, tenant, time, generated entry ID and established correlation/cause; the
explicit business call still chooses action, subject and disclosed details. Neither source
requires inventing an aggregate ID for an event with no aggregate. A nullable subject key,
with a meaningful subject type for global/system events, is a proposed consumer-compatible
representation. Object timelines can filter by tenant, subject type and subject key and order
by occurrence time plus entry ID; timestamp/UUID ordering is display order, not a proof of
database commit order.

Transactional accepted-change storage proves a narrower guarantee than a complete security
audit system. Denials, privileged retention, read authorization, tamper detection, protected
export and organization-specific obligations remain consumer decisions or future capabilities.
These sources do not justify treating E9 as standards compliance certification.

## Verification and extraction finding

This review adds cited research and checks existing source only. It runs no new executable
tests and proves no new reusable mechanism. The likely extraction remains explicit staging
and provider mapping; metadata defaults, business classification, disclosure and timeline
query policy belong to the sample unless separate consumer evidence earns a broader API.
