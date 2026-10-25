using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Consumer-owned transport composition. No Gloaming Kafka runtime or wire standard is introduced.
static class KafkaAdapter
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string Subscription = "kafka-example.effects.v1";
    internal const string Producer = "example-exporter-v1";

    internal static IProducer<byte[], byte[]> CreateProducer(string broker) =>
        new ProducerBuilder<byte[], byte[]>(
            new ProducerConfig
            {
                BootstrapServers = broker,
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageTimeoutMs = 5_000,
            }
        ).Build();

    internal static IConsumer<byte[], byte[]> CreateConsumer(string broker, string group)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = broker,
            GroupId = group,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            // An explicit fixture bootstrap choice, never an inferred production/replay policy.
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SessionTimeoutMs = 6_000,
            HeartbeatIntervalMs = 1_000,
            MaxPollIntervalMs = 10_000,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.Range,
        };
        config.Set("group.protocol", "classic");
        return new ConsumerBuilder<byte[], byte[]>(config)
            .SetPartitionsRevokedHandler((_, _) => { })
            .SetPartitionsAssignedHandler(
                (_, partitions) => Console.WriteLine("ASSIGNED:" + string.Join(',', partitions))
            )
            .Build();
    }

    internal static byte[] Encode(OutgoingMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new WireEnvelope(
                message.MessageId,
                message.MessageName,
                message.SchemaVersion,
                message.Payload,
                message.TenantKey,
                message.CorrelationId,
                message.CausationId,
                message.TraceParent,
                message.TraceState
            ),
            Json
        );

    internal static IncomingMessage Decode(ConsumeResult<byte[], byte[]> record)
    {
        try
        {
            if (record.Message.Value is null)
                throw new InvalidDataException("invalid-envelope");

            var wire = JsonSerializer.Deserialize<WireEnvelope>(record.Message.Value, Json);
            if (
                wire is null
                || wire.MessageId == Guid.Empty
                || wire.MessageName != "example.changed"
                || wire.SchemaVersion != 1
                || wire.Payload.GetProperty("item").GetGuid() == Guid.Empty
                || wire.Payload.GetProperty("amount").GetInt32() <= 0
            )
                throw new InvalidDataException("invalid-envelope");

            // Assign origin from the admitted native route. Wire fields and TenantKey grant no authority.
            return new IncomingMessage(
                wire.MessageId,
                Producer,
                wire.MessageName,
                wire.SchemaVersion,
                wire.Payload,
                wire.TenantKey,
                wire.CorrelationId,
                wire.CausationId,
                wire.TraceParent,
                wire.TraceState
            );
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or ArgumentException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            throw new InvalidDataException("invalid-envelope", error);
        }
    }

    internal static async Task<InboxReceiveResult> ReceiveAsync(
        ConsumeResult<byte[], byte[]> record,
        IServiceScopeFactory scopes,
        CancellationToken token
    )
    {
        var incoming = Decode(record);
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ReceiverDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            token
        );
        var result = await scope
            .ServiceProvider.GetRequiredService<IInbox<ReceiverDatabase>>()
            .ReceiveAsync(Subscription, incoming, token);
        await transaction.CommitAsync(token);
        return result; // The native caller may now commit exactly this consumed record's offset.
    }

    internal static async Task QuarantineAsync(
        ConsumeResult<byte[], byte[]> record,
        string reason,
        string sourceIncarnation,
        IServiceScopeFactory scopes,
        CancellationToken token
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIncarnation);
        if (reason is not ("identity-conflict" or "invalid-envelope"))
            throw new ArgumentOutOfRangeException(nameof(reason));

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ReceiverDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            token
        );
        var headers =
            record
                .Message.Headers?.Select(header => new RetainedHeader(
                    header.Key,
                    header.GetValueBytes() is { } bytes ? Convert.ToBase64String(bytes) : null
                ))
                .ToArray()
            ?? [];
        var key = new object[]
        {
            sourceIncarnation,
            record.Topic,
            record.Partition.Value,
            record.Offset.Value,
        };
        var existing = await database.Set<QuarantineRow>().FindAsync(key, token);
        if (existing is null)
            database.Add(
                new QuarantineRow
                {
                    Source = sourceIncarnation,
                    Topic = record.Topic,
                    Partition = record.Partition.Value,
                    Offset = record.Offset.Value,
                    Raw = record.Message.Value?.ToArray(),
                    RecordKey = record.Message.Key?.ToArray(),
                    HeadersJson = JsonSerializer.Serialize(headers, Json),
                    Timestamp = record.Message.Timestamp.UnixTimestampMs,
                    Reason = reason,
                }
            );
        else if (
            !Equal(existing.Raw, record.Message.Value)
            || !Equal(existing.RecordKey, record.Message.Key)
            || existing.Reason != reason
            || existing.Timestamp != record.Message.Timestamp.UnixTimestampMs
            || !JsonSerializer
                .Deserialize<RetainedHeader[]>(existing.HeadersJson, Json)!
                .SequenceEqual(headers)
        )
            throw new InvalidDataException("conflicting-quarantine-evidence");

        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private static bool Equal(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private sealed record RetainedHeader(string Name, string? Base64Value);
}

sealed class KafkaPublisher(IProducer<byte[], byte[]> producer, string topic) : IMessagePublisher
{
    public async Task PublishAsync(OutgoingMessage message, CancellationToken token)
    {
        if (message.RouteKey != topic)
            throw new InvalidDataException("unknown-route");

        var result = await producer.ProduceAsync(
            topic,
            new Message<byte[], byte[]>
            {
                Key = Encoding.UTF8.GetBytes(
                    message.Payload.GetProperty("item").GetGuid().ToString()
                ),
                Value = KafkaAdapter.Encode(message),
            },
            token
        );
        if (result.Status != PersistenceStatus.Persisted)
            throw new InvalidOperationException("Broker acceptance is unconfirmed.");
    }
}
