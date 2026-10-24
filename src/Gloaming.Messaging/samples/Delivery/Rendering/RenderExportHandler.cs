using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Messaging.Rendering;

public sealed class RenderExportHandler(RenderDbContext database) : IInboxHandler<RenderDbContext>
{
    public const string Subscription = "rendering.jobs";

    // Receiver-local DTO: the wire alias/schema is the contract, not a peer implementation reference.
    private sealed record RenderCommand(Guid ExportRequestId, int PageCount);

    private static readonly JsonEventCodec<RenderCommand> Codec = new(
        new JsonSerializerOptions { RespectRequiredConstructorParameters = true },
        [EventRegistration<RenderCommand>.For<RenderCommand>("exports.render", 2)],
        [new RenderV1ToV2()]
    );

    public static void Validate(IncomingMessage message) => Decode(message);

    private static RenderCommand Decode(IncomingMessage message)
    {
        if (
            message.ProducerKey != "exports"
            || message.MessageName != "exports.render"
            || message.TenantKey is not null
        )
            throw new InvalidDataException(
                "This receiver accepts tenantless exports.render from its configured producer only."
            );

        RenderCommand command;
        try
        {
            command = Codec.Deserialize(
                message.MessageName,
                message.SchemaVersion,
                message.Payload
            );
        }
        catch (EventDecodingException failure)
        {
            throw new InvalidDataException(
                "An unsupported or invalid render command was supplied.",
                failure
            );
        }

        if (command.ExportRequestId == Guid.Empty || command.PageCount <= 0)
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
            database.Add(RenderJob.Create(command.ExportRequestId, command.PageCount));
        else if (existing.Pages != command.PageCount)
            throw new InvalidDataException(
                "An export job identity was reused with different rendering requirements."
            );

        // Identical semantic work can arrive under another delivery ID. Retain each delivery,
        // but the receiver's business key prevents a second job.
    }
}
