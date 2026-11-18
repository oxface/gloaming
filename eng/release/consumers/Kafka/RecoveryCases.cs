using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

static partial class KafkaJourney
{
    private static async Task RecoveryCasesAsync(
        BrokerProxy proxy,
        string prefix,
        string groupPrefix
    )
    {
        string topic = prefix + "-network",
            group = groupPrefix + "-network";
        await TopicAsync(topic);
        var message = Message(topic, 17);
        await using (var sender = Sender())
        {
            await using var transaction = await sender.Database.BeginTransactionAsync(Token);
            new EfOutbox<SenderDatabase>(sender).Enqueue(message);
            await sender.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        proxy.DropProduceResponses = true;
        try
        {
            await using var sender = Sender();
            using var producer = KafkaAdapter.CreateProducer(Broker);
            try
            {
                await new PostgresOutboxDispatcher<SenderDatabase>(
                    sender,
                    new KafkaPublisher(producer, topic),
                    new(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(100))
                ).DispatchNextAsync(Token);
                throw new InvalidOperationException("Expected unobserved broker acknowledgement.");
            }
            catch (ProduceException<byte[], byte[]> error)
                when (error.Error.Code == ErrorCode.Local_MsgTimedOut) { }
        }
        finally
        {
            proxy.DropProduceResponses = false;
        }

        Check(proxy.DroppedResponses > 0, "The wire fault must drop an actual Produce response.");
        using (var observer = Consumer(group))
            Check(
                observer
                    .QueryWatermarkOffsets(new TopicPartition(topic, 0), TimeSpan.FromSeconds(10))
                    .High.Value == 1,
                "The broker must retain the native idempotent producer's accepted message despite missing acknowledgements."
            );
        await using (var observer = Sender())
        {
            var row = await observer
                .Set<OutboxMessageRecord>()
                .SingleAsync(row => row.MessageId == message.MessageId, Token);
            Check(
                row.DispatchedAt is null && row.Attempts == 1,
                "Unconfirmed publication must not complete its retained outbox row."
            );
        }

        await Task.Delay(150, Token);
        await using (var sender = Sender())
        {
            using var producer = KafkaAdapter.CreateProducer(Broker);
            Check(
                await new PostgresOutboxDispatcher<SenderDatabase>(
                    sender,
                    new KafkaPublisher(producer, topic),
                    new(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(100))
                ).DispatchNextAsync(Token) == OutboxDispatchResult.Published,
                "Fresh native producer must recover the failed confirmed-state update."
            );
        }

        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            for (int offset = 0; offset < 2; offset++)
            {
                var record = Next(consumer);
                Check(
                    record.Offset.Value == offset
                        && KafkaAdapter.Decode(record).MessageId == message.MessageId,
                    "Acknowledgement loss and fresh publication must preserve the exact delivery identity."
                );
                Check(
                    await ReceiveAsync(record)
                        == (
                            offset == 0
                                ? InboxReceiveResult.Queued
                                : InboxReceiveResult.AlreadyReceived
                        ),
                    "Lost acknowledgements must converge to one durable receipt."
                );
                consumer.Commit(record);
            }

            consumer.Close();
        }

        await using (var services = Services(new FailureSwitch()))
            Check(
                await ProcessAsync(services) == InboxProcessingResult.Processed,
                "Network-uncertain delivery must process once."
            );
        await ExpectedEffectAsync(message, 17);
        Pass(
            "broker-acknowledgement-response-loss",
            new
            {
                droppedResponses = proxy.DroppedResponses,
                brokerCopies = 2,
                effects = 1,
            }
        );

        topic = prefix + "-commit";
        group = groupPrefix + "-commit";
        await TopicAsync(topic);
        message = Message(topic, 19);
        await PublishAsync(topic, KafkaAdapter.Encode(message));
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            var record = Next(consumer);
            await using var services = Services(
                new FailureSwitch(),
                new CommitObservation("lose-observation")
            );
            try
            {
                await KafkaAdapter.ReceiveAsync(
                    record,
                    services.GetRequiredService<IServiceScopeFactory>(),
                    Token
                );
                throw new InvalidOperationException("Expected caller-unobserved database commit.");
            }
            catch (CommitObservationLost) { }

            consumer.Close();
        }

        Check(
            Committed(topic, group) < 0,
            "A thrown commit observation must leave Kafka unacknowledged."
        );
        await using (var observer = Receiver())
            Check(
                await observer
                    .Set<InboxMessageRecord>()
                    .AnyAsync(
                        row => row.MessageId == message.MessageId && row.ProcessedAt == null,
                        Token
                    ),
                "Actual native commit must have retained the receipt despite the post-commit fault."
            );
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            var record = Next(consumer);
            Check(
                record.Offset.Value == 0
                    && await ReceiveAsync(record) == InboxReceiveResult.AlreadyReceived,
                "A fresh intake must reconcile uncertain commit from retained storage."
            );
            consumer.Commit(record);
            consumer.Close();
        }

        await using (var services = Services(new FailureSwitch()))
            Check(
                await ProcessAsync(services) == InboxProcessingResult.Processed,
                "Reconciled receipt must process once."
            );
        await ExpectedEffectAsync(message, 19);
        Pass(
            "caller-unobserved-native-database-commit",
            new { nextOffset = Committed(topic, group), effects = 1 }
        );

        topic = prefix + "-restart";
        group = groupPrefix + "-restart";
        await TopicAsync(topic);
        message = Message(topic, 23);
        await PublishAsync(topic, KafkaAdapter.Encode(message));
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            var record = Next(consumer);
            Check(
                await ReceiveAsync(record) == InboxReceiveResult.Queued,
                "Pre-restart intake must commit."
            );
            consumer.Commit(record);
            consumer.Close();
        }

        Console.WriteLine("CONTROL:restart-broker");
        await Console.Out.FlushAsync(Token);
        Check(
            await Console.In.ReadLineAsync(Token) == "restarted",
            "Runner must confirm restart of its owned broker."
        );
        Check(
            Committed(topic, group) == 1,
            "Broker restart must preserve its committed consumer position."
        );
        Check(
            await PublishAsync(topic, KafkaAdapter.Encode(message)) == 1,
            "Retained broker log must continue after restart."
        );
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            var record = Next(consumer);
            Check(
                record.Offset.Value == 1
                    && await ReceiveAsync(record) == InboxReceiveResult.AlreadyReceived,
                "Restart must preserve receipt and group progress independently."
            );
            consumer.Commit(record);
            consumer.Close();
        }

        await using (var services = Services(new FailureSwitch()))
            Check(
                await ProcessAsync(services) == InboxProcessingResult.Processed,
                "Retained pre-restart work must process once."
            );
        await ExpectedEffectAsync(message, 23);
        Pass("native-broker-restart", new { nextOffset = Committed(topic, group), effects = 1 });

        topic = prefix + "-cancel";
        group = groupPrefix + "-cancel";
        await TopicAsync(topic);
        using (var consumer = Consumer(group))
        using (var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            consumer.Subscribe(topic);
            using var assignment = CancellationTokenSource.CreateLinkedTokenSource(Token);
            assignment.CancelAfter(TimeSpan.FromSeconds(30));
            while (consumer.Assignment.Count == 0)
            {
                assignment.Token.ThrowIfCancellationRequested();
                Check(
                    consumer.Consume(TimeSpan.FromMilliseconds(100)) is null,
                    "The new cancellation topic must contain no records."
                );
            }

            cancelled.CancelAfter(TimeSpan.FromMilliseconds(200));
            try
            {
                await Task.Run(() => consumer.Consume(cancelled.Token), Token)
                    .WaitAsync(TimeSpan.FromSeconds(10), Token);
                throw new InvalidOperationException("Empty native consume must be cancelled.");
            }
            catch (OperationCanceledException)
                when (cancelled.IsCancellationRequested && !Token.IsCancellationRequested) { }

            consumer.Close();
        }

        Check(
            Committed(topic, group) < 0,
            "Cancelled consumption and close must not invent an acknowledged position."
        );
        Pass(
            "cancelled-consume-and-native-close",
            new { committedOffset = Committed(topic, group) }
        );

        byte[] literal = await File.ReadAllBytesAsync("Fixtures/changed.v1.json", Token);
        Check(
            await PublishAsync(topic, literal) == 0,
            "Literal retained v1 record must be first after cancellation."
        );
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            var record = Next(consumer);
            Check(
                record.Message.Value.SequenceEqual(literal),
                "The Kafka record must preserve the owned historical bytes."
            );
            var retained = KafkaAdapter.Decode(record);
            Check(
                retained.MessageId == Guid.Parse("79276e88-579b-4669-85ae-4d1671916471")
                    && retained.Payload.GetProperty("item").GetGuid()
                        == Guid.Parse("fa507fa6-787c-4c5e-959f-e4fce88333da")
                    && retained.Payload.GetProperty("amount").GetInt32() == 29,
                "Literal expectations must be independent of the encoder."
            );
            Check(
                await ReceiveAsync(record) == InboxReceiveResult.Queued,
                "Fresh operation after native close must accept literal v1."
            );
            consumer.Commit(record);
            consumer.Close();
        }

        await using (var services = Services(new FailureSwitch()))
            Check(
                await ProcessAsync(services) == InboxProcessingResult.Processed,
                "Literal reader must produce its expected effect."
            );
        await using (var observer = Receiver())
        {
            var effect = await observer
                .Set<EffectRow>()
                .SingleAsync(
                    row => row.Id == Guid.Parse("fa507fa6-787c-4c5e-959f-e4fce88333da"),
                    Token
                );
            Check(
                effect.Amount == 29
                    && effect.MessageId == Guid.Parse("79276e88-579b-4669-85ae-4d1671916471"),
                "Persisted literal outcome must match independently owned identities and values."
            );
        }

        Pass(
            "owned-wire-v1-and-post-close-recovery",
            new { nextOffset = Committed(topic, group), amount = 29 }
        );
    }

    private static async Task TopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = Broker }
        ).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1,
            },
        ]);
    }
}
