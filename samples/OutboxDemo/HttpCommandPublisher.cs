using System.Net.Http.Json;
using Gloaming.Messaging;

namespace ModulithFoundry.Samples.OutboxDemo;

/// <summary>This consumer maps logical destinations to configured endpoints and defines accepted HTTP status.</summary>
public sealed class HttpCommandPublisher(HttpClient client, Uri receiver) : IMessagePublisher
{
    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (
            message.RouteKey != "exports.render"
            || message.MessageName != "exports.render"
            || message.SchemaVersion is not (1 or 2)
        )
            throw new InvalidDataException(
                "This publisher handles the exports.render v1/v2 destination only."
            );

        using var request = new HttpRequestMessage(HttpMethod.Post, receiver);
        request.Headers.Add("X-Message-Id", message.MessageId.ToString());
        request.Headers.Add("X-Message-Name", message.MessageName);
        if (message.TenantKey is not null)
            request.Headers.Add("X-Tenant-Key", message.TenantKey);
        if (message.CorrelationId is not null)
            request.Headers.Add("X-Correlation-Id", message.CorrelationId);
        if (message.CausationId is not null)
            request.Headers.Add("X-Causation-Id", message.CausationId);
        request.Headers.Add(
            "X-Message-Schema",
            message.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        request.Content = JsonContent.Create(message.Payload);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
