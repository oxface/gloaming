using System.Diagnostics;
using System.Text;
using Gloaming.Messaging;
using RabbitMQ.Client;

namespace Gloaming.Samples.Messaging.Worker.Transport;

// Logical routing is consumer policy; the physical queue is explicitly configured.
public sealed class RabbitMqExportPublisher(IChannel channel, string queue) : IMessagePublisher
{
    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (
            message.RouteKey != "exports.render"
            || message.MessageName != "exports.render"
            || message.SchemaVersion is not (1 or 2)
        )
            throw new InvalidDataException(
                "Only exports.render v1/v2 is routed by this publisher."
            );

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.MessageId.ToString(),
            Type = message.MessageName,
            CorrelationId = message.CorrelationId,
            Headers = new Dictionary<string, object?>
            {
                ["producer-module"] = "exports",
                ["schema-version"] = message.SchemaVersion,
            },
        };
        if (message.TenantKey is not null)
            properties.Headers["tenant-key"] = message.TenantKey;
        if (message.CausationId is not null)
            properties.Headers["causation-id"] = message.CausationId;

        // Prefer the active publication context as one coherent parent/state pair. The
        // native client can replace the parent with its child send span, which inherits
        // the same state. Without an activity, fall back to explicitly retained context.
        var current = Activity.Current;
        string? traceParent = current?.Id ?? message.TraceParent;
        string? traceState = current is null ? message.TraceState : current.TraceStateString;
        if (traceParent is not null)
            properties.Headers["traceparent"] = traceParent;
        if (traceState is not null)
            properties.Headers["tracestate"] = traceState;

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(5));
        await channel.BasicPublishAsync(
            "",
            queue,
            mandatory: true,
            properties,
            Encoding.UTF8.GetBytes(message.Payload.GetRawText()),
            bounded.Token
        );
    }
}
