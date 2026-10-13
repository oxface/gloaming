using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rootbolt.Messaging.Workflows.EntityFrameworkCore;

// Wire names, request identity and semantic outcomes survive delivery-ID and trace changes.
internal sealed record WorkflowOperation(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string ContinuationName,
    [property: JsonRequired] string RequestName,
    WorkflowOutcome? Outcome
);

internal sealed record WorkflowOutcome(
    [property: JsonRequired] string ProducerKey,
    [property: JsonRequired] string MessageName,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] JsonElement Payload
)
{
    internal bool Matches(IncomingMessage reply) =>
        ProducerKey == reply.ProducerKey
        && MessageName == reply.MessageName
        && SchemaVersion == reply.SchemaVersion
        && JsonElement.DeepEquals(Payload, reply.Payload);
}
