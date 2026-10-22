using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Messaging.Exports;
using ModulithFoundry.Samples.Messaging.Rendering;
using ModulithFoundry.Samples.Messaging.Worker.Transport;
using RabbitMQ.Client;

namespace ModulithFoundry.Samples.Messaging.Tests.Fixtures.Broker;

// Finite consumer rollout rehearsal. Each module retains its own transactions and storage.
public static class SchemaCompatibilityJourney
{
    public static async Task RunAsync(
        string senderConnection,
        string receiverConnection,
        Uri broker,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using (var sender = ExportDbContext.Create(senderConnection))
        {
            await sender.Database.MigrateAsync(cancellationToken);
            if (
                await sender.Set<ExportRequest>().AnyAsync(cancellationToken)
                || await sender.Set<OutboxMessageRecord>().AnyAsync(cancellationToken)
            )
                throw new InvalidOperationException(
                    "Use disposable empty Exports storage for this journey."
                );
        }

        await using (var receiver = RenderDbContext.Create(receiverConnection))
        {
            await receiver.Database.MigrateAsync(cancellationToken);
            if (
                await receiver.Set<RenderJob>().AnyAsync(cancellationToken)
                || await receiver.Set<InboxMessageRecord>().AnyAsync(cancellationToken)
            )
                throw new InvalidOperationException(
                    "Use disposable empty Rendering storage for this journey."
                );
        }

        var factory = new ConnectionFactory { Uri = broker, AutomaticRecoveryEnabled = false };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            cancellationToken
        );
        string queue = "exports.compatibility." + Guid.NewGuid().ToString("N");
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken
        );

        var old = await SubmitAsync(senderConnection, schema: 1, pages: 3, cancellationToken);
        await using (var oldIntake = Reader(receiverConnection))
            await TransferAsync(
                senderConnection,
                channel,
                queue,
                oldIntake,
                old,
                output,
                cancellationToken
            );

        // The original scopes are gone and v1 intake is still pending when v2 writes begin.
        var current = await SubmitAsync(senderConnection, schema: 2, pages: 5, cancellationToken);
        await using var upgraded = Reader(receiverConnection);
        await TransferAsync(
            senderConnection,
            channel,
            queue,
            upgraded,
            current,
            output,
            cancellationToken
        );
        for (int index = 0; index < 2; index++)
        {
            await using var scope = upgraded.CreateAsyncScope();
            if (
                await scope
                    .ServiceProvider.GetRequiredService<IInboxProcessor<RenderDbContext>>()
                    .ProcessNextAsync(RenderExportHandler.Subscription, cancellationToken)
                != InboxProcessingResult.Processed
            )
                throw new InvalidOperationException("Expected one pending render command.");
        }

        await using var finalSender = ExportDbContext.Create(senderConnection);
        await using var finalReceiver = RenderDbContext.Create(receiverConnection);
        foreach (var original in new[] { old, current })
        {
            var outgoing = await finalSender
                .Set<OutboxMessageRecord>()
                .SingleAsync(row => row.MessageId == original.MessageId, cancellationToken);
            var incoming = await finalReceiver
                .Set<InboxMessageRecord>()
                .SingleAsync(row => row.MessageId == original.MessageId, cancellationToken);
            if (
                outgoing.DispatchedAt is null
                || incoming.ProcessedAt is null
                || outgoing.SchemaVersion != original.SchemaVersion
                || incoming.SchemaVersion != original.SchemaVersion
                || !JsonElement.DeepEquals(original.Payload, outgoing.Payload)
                || !JsonElement.DeepEquals(original.Payload, incoming.Payload)
            )
                throw new InvalidOperationException(
                    "The completed work must retain its original envelope."
                );
        }

        if (await finalReceiver.Set<RenderJob>().CountAsync(cancellationToken) != 2)
            throw new InvalidOperationException("Expected two local render jobs.");

        await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: true, cancellationToken);
        output.WriteLine("retained schemas=1,2; local jobs=2; original envelopes unchanged");
    }

    private static ServiceProvider Reader(string connection)
    {
        var services = new ServiceCollection();
        RenderServices.AddRendering(services, connection);
        return services.BuildServiceProvider();
    }

    private static async Task<OutgoingMessage> SubmitAsync(
        string connection,
        int schema,
        int pages,
        CancellationToken cancellation
    )
    {
        await using var database = ExportDbContext.Create(connection);
        Guid id = Guid.NewGuid();
        database.Add(ExportRequest.Create(id, pages));
        await database.SaveChangesAsync(cancellation);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellation);
        var commands = new ExportRequestCommands(
            database,
            new EfOutbox<ExportDbContext>(database),
            schema
        );
        if (await commands.SubmitAsync(id, 1, cancellation) != ExportSubmissionResult.Accepted)
            throw new InvalidOperationException("The export draft was not accepted.");

        await database.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        var row = await database
            .Set<OutboxMessageRecord>()
            .SingleAsync(row => row.SchemaVersion == schema, cancellation);
        return new(
            row.MessageId,
            row.RouteKey,
            row.MessageName,
            row.SchemaVersion,
            row.Payload,
            row.TenantKey,
            row.CorrelationId,
            row.CausationId,
            row.TraceParent,
            row.TraceState
        );
    }

    private static async Task TransferAsync(
        string connection,
        IChannel channel,
        string queue,
        IServiceProvider reader,
        OutgoingMessage original,
        TextWriter output,
        CancellationToken cancellation
    )
    {
        await using (var database = ExportDbContext.Create(connection))
        {
            var dispatcher = new PostgresOutboxDispatcher<ExportDbContext>(
                database,
                new RabbitMqExportPublisher(channel, queue),
                new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1))
            );
            if (await dispatcher.DispatchNextAsync(cancellation) != OutboxDispatchResult.Published)
                throw new InvalidOperationException("Expected a retained outgoing render command.");
        }

        var delivery =
            await channel.BasicGetAsync(queue, autoAck: false, cancellation)
            ?? throw new InvalidOperationException("No confirmed render command was available.");
        var envelope = RabbitMqRenderReceiver.Parse(delivery);
        if (
            envelope.MessageId != original.MessageId
            || envelope.SchemaVersion != original.SchemaVersion
            || !JsonElement.DeepEquals(original.Payload, envelope.Payload)
        )
            throw new InvalidOperationException(
                "Publication must preserve the stored wire contract."
            );

        var intake = new RabbitMqRenderReceiver(
            channel,
            reader.GetRequiredService<IServiceScopeFactory>()
        );
        if (await intake.ReceiveAsync(delivery, cancellation) != InboxReceiveResult.Queued)
            throw new InvalidOperationException("Expected newly retained incoming work.");

        output.WriteLine(
            $"schema {original.SchemaVersion}: delivery {original.MessageId} retained"
        );
    }
}
