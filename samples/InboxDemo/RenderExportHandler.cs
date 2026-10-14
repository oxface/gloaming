using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.InboxDemo;

public sealed class RenderExportHandler(RenderDbContext database) : IInboxHandler<RenderDbContext>
{
    public const string Subscription = "rendering.jobs";

    // Receiver-local DTO: the wire alias/schema is the contract, not a peer implementation reference.
    private sealed record RenderCommand(Guid ExportRequestId, int Pages);

    public static void Validate(IncomingMessage message) => Decode(message);

    private static RenderCommand Decode(IncomingMessage message)
    {
        if (
            message.ProducerKey != "exports"
            || message.MessageName != "exports.render"
            || message.SchemaVersion != 1
            || message.TenantKey is not null
        )
            throw new InvalidDataException(
                "This receiver accepts tenantless exports.render v1 from its configured producer only."
            );

        var command =
            message.Payload.Deserialize<RenderCommand>()
            ?? throw new InvalidDataException("A render command is required.");
        if (command.ExportRequestId == Guid.Empty || command.Pages <= 0)
            throw new InvalidDataException(
                "A render command needs an export identity and positive pages."
            );

        return command;
    }

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        var command = Decode(message);
        var existing = await database
            .Set<RenderJob>()
            .SingleOrDefaultAsync(
                row => row.ExportRequestId == command.ExportRequestId,
                cancellationToken
            );
        if (existing is null)
            database.Add(RenderJob.Create(command.ExportRequestId, command.Pages));
        else if (existing.Pages != command.Pages)
            throw new InvalidDataException(
                "An export job identity was reused with different rendering requirements."
            );

        // Identical semantic work can arrive under another delivery ID. Retain each delivery,
        // but the receiver's business key prevents a second job.
    }
}
