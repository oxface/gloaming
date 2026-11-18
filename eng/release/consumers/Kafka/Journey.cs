using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

await KafkaJourney.RunAsync(args);

static partial class KafkaJourney
{
    internal static readonly string Broker = Required("PROBE_KAFKA");
    internal static readonly string Connection = Required("PROBE_POSTGRES");
    private static readonly string SourceIncarnation = Required("GLOAMING_PROOF_SOURCE");
    private static readonly Timestamp RecordTimestamp = new(DateTime.UtcNow);
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private const string TraceState = "example=retained";
    private static readonly CancellationTokenSource Deadline = new(TimeSpan.FromMinutes(6));
    internal static CancellationToken Token => Deadline.Token;
    private static readonly List<object> Results = [];
    private static readonly int[] QuarantineOffsets = [2, 4];
    private static readonly int[] ExpectedAmounts = [7, 11, 13, 17, 19, 23, 29];
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException("Missing " + key);

    internal static SenderDatabase Sender() =>
        new(new DbContextOptionsBuilder<SenderDatabase>().UseNpgsql(Connection).Options);

    internal static ReceiverDatabase Receiver() =>
        new(new DbContextOptionsBuilder<ReceiverDatabase>().UseNpgsql(Connection).Options);

    internal static ServiceProvider Services(
        FailureSwitch failure,
        CommitObservation? observation = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(failure);
        services.AddDbContext<ReceiverDatabase>(options =>
        {
            options.UseNpgsql(Connection);
            if (observation is not null)
                options.AddInterceptors(observation);
        });
        services.AddPostgresInbox<ReceiverDatabase>();
        services.AddPostgresInboxProcessor<ReceiverDatabase>(new(TimeSpan.FromMilliseconds(100)));
        services.AddInboxHandler<ReceiverDatabase, EffectHandler>(KafkaAdapter.Subscription);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    internal static async Task BarrierAsync(long offset)
    {
        Console.WriteLine("BARRIER:" + offset);
        await Console.Out.FlushAsync(Token);
        await Console.In.ReadLineAsync(Token);
    }

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length > 0)
        {
            await ChildAsync(args);
            return;
        }

        await using (var sender = Sender())
            await sender.Database.ExecuteSqlRawAsync(sender.Database.GenerateCreateScript(), Token);
        await using (var receiver = Receiver())
            await receiver.Database.ExecuteSqlRawAsync(
                receiver.Database.GenerateCreateScript(),
                Token
            );

        await using var proxy = new BrokerProxy(
            Required("PROBE_KAFKA_UPSTREAM"),
            new Uri("tcp://" + Broker).Port
        );
        proxy.Start();
        string topic = "gloaming-kafka-" + Guid.NewGuid().ToString("N");
        string group = topic + "-group";
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = Broker }
        ).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1,
                Configs = new Dictionary<string, string> { ["retention.ms"] = "3600000" },
            },
        ]);
        var message = Message(topic, 7);
        await using (var sender = Sender())
        {
            await using var transaction = await sender.Database.BeginTransactionAsync(Token);
            sender.Add(
                new RequestRow { Id = message.Payload.GetProperty("item").GetGuid(), Amount = 7 }
            );
            new EfOutbox<SenderDatabase>(sender).Enqueue(message);
            await sender.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        // Broker accepts offset 0, then the publisher process dies before completing the outbox claim.
        await using (var crash = ChildHost.Start("publish", topic, group))
        {
            Check(
                await crash.BarrierAsync(Token) == 0,
                "Publisher must reach the post-acceptance barrier."
            );
            await crash.KillAsync();
        }

        await using (var observer = Sender())
        {
            var retained = await observer.Set<OutboxMessageRecord>().SingleAsync(Token);
            Check(
                retained.MessageId == message.MessageId
                    && retained.DispatchedAt is null
                    && retained.Attempts == 1,
                "Accepted-but-uncompleted publication must leave its original outbox claim."
            );
        }

        // Use the database's retained expiry; the test does not assume a client clock for eligibility.
        using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            bounded.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                await using var sender = Sender();
                using var producer = KafkaAdapter.CreateProducer(Broker);
                var result = await new PostgresOutboxDispatcher<SenderDatabase>(
                    sender,
                    new KafkaPublisher(producer, topic),
                    new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100))
                ).DispatchNextAsync(bounded.Token);
                if (result == OutboxDispatchResult.Published)
                    break;

                Check(result == OutboxDispatchResult.NoWork, "Unexpected reclaim outcome.");
                await Task.Delay(100, bounded.Token);
            }
        }

        await using (var observer = Sender())
        {
            var retained = await observer.Set<OutboxMessageRecord>().SingleAsync(Token);
            Check(
                retained.MessageId == message.MessageId
                    && retained.DispatchedAt is not null
                    && retained.Attempts == 2,
                "Fresh producer must complete the same retained delivery after expired-claim recovery."
            );
            Check(
                (await observer.Set<RequestRow>().SingleAsync(Token)).Amount == 7,
                "Original accepted producer state must remain committed."
            );
        }

        Pass("publication-crash-recovery", new { attempts = 2, originalId = message.MessageId });

        // An inserted but uncommitted inbox row vanishes when the consumer is killed.
        await using (var crash = ChildHost.Start("before-commit", topic, group))
        {
            Check(await crash.BarrierAsync(Token) == 0, "Intake must start at original offset 0.");
            await CountsAsync(0, 0, 0);
            Check(
                Committed(topic, group) < 0,
                "Uncommitted intake must not advance Kafka offsets."
            );
            await crash.KillAsync();
        }

        await CountsAsync(0, 0, 0);
        Pass(
            "intake-crash-before-database-commit",
            new { committedOffset = Committed(topic, group) }
        );

        // Database commit survives death before the independent Kafka offset commit.
        await using (var crash = ChildHost.Start("after-commit", topic, group))
        {
            Check(
                await crash.BarrierAsync(Token) == 0,
                "Replacement must redeliver the unaccepted offset."
            );
            await CountsAsync(1, 0, 0);
            Check(
                Committed(topic, group) < 0,
                "Database-only acceptance must leave offset retryable."
            );
            await crash.KillAsync();
        }

        Pass(
            "intake-crash-after-database-commit",
            new { committedOffset = Committed(topic, group) }
        );

        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            for (int offset = 0; offset < 2; offset++)
            {
                var record = Next(consumer);
                Check(
                    record.Offset.Value == offset,
                    "Both broker copies must remain available in order."
                );
                Check(
                    KafkaAdapter.Decode(record).MessageId == message.MessageId,
                    "Publication retry must retain identity."
                );
                Check(
                    await ReceiveAsync(record) == InboxReceiveResult.AlreadyReceived,
                    "Crash replay and producer retry must converge to the original receipt."
                );
                consumer.Commit(record);
            }

            consumer.Close();
        }

        Check(
            Committed(topic, group) == 2,
            "Broker must retain next offset 2 only after both durable receipts."
        );
        await CountsAsync(1, 0, 0);
        var failure = new FailureSwitch { FailAfterSave = true };
        await using (var provider = Services(failure))
        {
            try
            {
                await ProcessAsync(provider);
                throw new InvalidOperationException(
                    "Injected post-SQL handler failure was not observed."
                );
            }
            catch (InjectedHandlerFailure) { }
        }

        await CountsAsync(1, 0, 0);
        await using (var observer = Receiver())
        {
            var receipt = await observer.Set<InboxMessageRecord>().SingleAsync(Token);
            Check(
                receipt.ProcessedAt is null && receipt.FailureCount == 1,
                "Failed local effect must roll back while retaining independent retry accounting."
            );
        }

        await Task.Delay(150, Token);
        await using (var provider = Services(new FailureSwitch()))
        {
            Check(
                await ProcessAsync(provider) == InboxProcessingResult.Processed,
                "Fresh local attempt must commit."
            );
            Check(
                await ProcessAsync(provider) == InboxProcessingResult.NoWork,
                "Duplicate publications must not queue extra effects."
            );
        }

        await ExpectedEffectAsync(message, 7);
        Pass(
            "duplicate-intake-and-local-rollback",
            new
            {
                retainedReceipts = 1,
                committedEffects = 1,
                nextOffset = 2,
            }
        );

        // An identity conflict blocks progress. A later valid record must not be acknowledged over it.
        var conflict = new OutgoingMessage(
            message.MessageId,
            topic,
            message.MessageName,
            1,
            JsonSerializer.SerializeToElement(
                new { item = message.Payload.GetProperty("item").GetGuid(), amount = 999 }
            ),
            message.TenantKey,
            message.CorrelationId,
            message.CausationId
        );
        var second = Message(topic, 11);
        Check(
            await PublishAsync(topic, KafkaAdapter.Encode(conflict)) == 2,
            "Conflict offset must be 2."
        );
        Check(
            await PublishAsync(topic, KafkaAdapter.Encode(second)) == 3,
            "Later valid offset must be 3."
        );
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            var record = Next(consumer);
            Check(record.Offset.Value == 2, "Restart must begin at the first unaccepted record.");
            try
            {
                await ReceiveAsync(record);
                throw new InvalidOperationException("Expected retained-content conflict.");
            }
            catch (InboxMessageConflictException) { }

            consumer.Close();
        }

        Check(
            Committed(topic, group) == 2,
            "Conflict must not advance past itself or later valid work."
        );
        await CountsAsync(1, 1, 0);
        Pass(
            "conflict-fail-stop-preserves-earlier-offset",
            new { nextOffset = 2, laterOffset = 3 }
        );

        byte[] malformed = "{\"messageId\":\"not-a-guid\"}"u8.ToArray();
        Check(await PublishAsync(topic, malformed) == 4, "Malformed record offset must be 4.");
        using (var consumer = Consumer(group))
        {
            consumer.Subscribe(topic);
            for (int offset = 2; offset <= 4; offset++)
            {
                var record = Next(consumer);
                Check(
                    record.Offset.Value == offset,
                    "Disposition must proceed sequentially from the blocked offset."
                );
                try
                {
                    Check(
                        await ReceiveAsync(record) == InboxReceiveResult.Queued,
                        "New valid record must enter durable intake."
                    );
                }
                catch (InboxMessageConflictException)
                {
                    await QuarantineAsync(record, "identity-conflict");
                    await QuarantineAsync(record, "identity-conflict");
                }
                catch (InvalidDataException)
                {
                    await QuarantineAsync(record, "invalid-envelope");
                    await QuarantineAsync(record, "invalid-envelope");
                }

                // Explicit fixture policy: commit only after receipt or durable raw quarantine.
                consumer.Commit(record);
            }

            consumer.Close();
        }

        Check(Committed(topic, group) == 5, "Durable dispositions must advance to next offset 5.");
        await CountsAsync(2, 1, 2);
        await using (var observer = Receiver())
        {
            var quarantined = await observer
                .Set<QuarantineRow>()
                .OrderBy(row => row.Offset)
                .ToArrayAsync(Token);
            Check(
                quarantined[0].Offset == 2
                    && quarantined[0].Reason == "identity-conflict"
                    && quarantined[0].Raw!.SequenceEqual(KafkaAdapter.Encode(conflict)),
                "Conflict bytes/disposition must be retained exactly."
            );
            Check(
                quarantined[1].Offset == 4
                    && quarantined[1].Reason == "invalid-envelope"
                    && quarantined[1].Raw!.SequenceEqual(malformed),
                "Malformed bytes/disposition must be retained exactly."
            );
            foreach (var row in quarantined)
            {
                Check(
                    row.Source == SourceIncarnation
                        && row.Topic == topic
                        && row.Partition == 0
                        && row.RecordKey!.AsSpan().SequenceEqual("fixture"u8)
                        && row.Timestamp == RecordTimestamp.UnixTimestampMs,
                    "Quarantine must retain the admitted source and native record metadata."
                );
                var headers = JsonSerializer.Deserialize<JsonElement>(row.HeadersJson);
                Check(
                    headers.GetArrayLength() == 3
                        && headers[0].GetProperty("name").GetString() == "example-tag"
                        && headers[0].GetProperty("base64Value").GetString() == "AQID"
                        && headers[1].GetProperty("name").GetString() == "empty-tag"
                        && headers[1].GetProperty("base64Value").ValueKind == JsonValueKind.Null
                        && headers[2].GetProperty("name").GetString() == "example-tag"
                        && headers[2].GetProperty("base64Value").GetString() == "BA==",
                    "Quarantine must retain duplicate header names, null values, byte values and their order."
                );
            }
        }

        await using (var provider = Services(new FailureSwitch()))
            Check(
                await ProcessAsync(provider) == InboxProcessingResult.Processed,
                "Valid work after quarantine must process."
            );
        await ExpectedEffectAsync(second, 11);
        Pass(
            "durable-poison-disposition",
            new { nextOffset = 5, quarantineOffsets = QuarantineOffsets }
        );

        // Live group ownership changes while the first owner has committed intake but not its offset.
        string rebalanceTopic = topic + "-rebalance";
        string rebalanceGroup = group + "-rebalance";
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = rebalanceTopic,
                NumPartitions = 1,
                ReplicationFactor = 1,
            },
        ]);
        var third = Message(rebalanceTopic, 13);
        Check(
            await PublishAsync(rebalanceTopic, KafkaAdapter.Encode(third)) == 0,
            "Rebalance topic begins at zero."
        );
        string staleCommitError;
        await using (var old = ChildHost.Start("rebalance", rebalanceTopic, rebalanceGroup))
        {
            Check(
                await old.BarrierAsync(Token) == 0,
                "First owner must durably accept offset zero."
            );
            using var successor = Consumer(rebalanceGroup);
            successor.Subscribe(rebalanceTopic);
            var record = Next(successor);
            Check(
                record.Offset.Value == 0
                    && await ReceiveAsync(record) == InboxReceiveResult.AlreadyReceived,
                "Successor must redeliver committed intake after max-poll ownership loss."
            );
            successor.Commit(record);
            Check(
                Committed(rebalanceTopic, rebalanceGroup) == 1,
                "Successor must commit only its accepted offset."
            );
            await old.ResumeAsync();
            string stale = await old.FinishAsync(Token);
            const string rejection = "STALE-COMMIT-REJECTED:";
            var line = stale
                .Split('\n')
                .SingleOrDefault(value => value.StartsWith(rejection, StringComparison.Ordinal));
            staleCommitError = line?[rejection.Length..] ?? "missing-rejection";
            Check(
                staleCommitError is "Local_State" or "UnknownMemberId",
                "Old owner's offset commit must fail with an ownership-loss error. " + stale
            );
            successor.Close();
        }

        Check(
            Committed(rebalanceTopic, rebalanceGroup) == 1,
            "Stale owner must leave successor offset intact."
        );
        await using (var provider = Services(new FailureSwitch()))
        {
            Check(
                await ProcessAsync(provider) == InboxProcessingResult.Processed,
                "Rebalanced receipt must process once."
            );
            Check(
                await ProcessAsync(provider) == InboxProcessingResult.NoWork,
                "No duplicate local effect may remain."
            );
        }

        await ExpectedEffectAsync(third, 13);
        await CountsAsync(3, 3, 2);
        Pass(
            "live-rebalance-and-stale-offset-fencing",
            new { nextOffset = 1, staleCommit = staleCommitError }
        );

        await RecoveryCasesAsync(proxy, topic, group);
        await using (var observer = Receiver())
        {
            var receipts = await observer.Set<InboxMessageRecord>().ToArrayAsync(Token);
            Check(
                receipts.All(row =>
                    row.ProducerKey == KafkaAdapter.Producer
                    && row.TenantKey == "fixture-tenant"
                    && row.CorrelationId == "probe-conversation"
                    && row.CausationId == "probe-cause"
                    && row.ProcessedAt is not null
                    && (
                        row.Payload.GetProperty("amount").GetInt32() == 29
                            ? row.TraceParent is null && row.TraceState is null
                            : row.TraceParent == TraceParent && row.TraceState == TraceState
                    )
                ),
                "Exact retained envelope attribution must survive all paths."
            );
            var effects = await observer
                .Set<EffectRow>()
                .OrderBy(row => row.Amount)
                .Select(row => row.Amount)
                .ToArrayAsync(Token);
            Check(
                effects.SequenceEqual(ExpectedAmounts),
                "Independent final effect oracle must have only expected amounts."
            );
        }

        await CountsAsync(7, 7, 2);
        Pass(
            "independent-final-database-oracle",
            new
            {
                receipts = 7,
                effects = ExpectedAmounts,
                quarantine = 2,
            }
        );
        await File.WriteAllTextAsync(
            Required("PROBE_RESULT"),
            JsonSerializer.Serialize(
                new
                {
                    checkpoint = Required("GLOAMING_PROOF_REVISION"),
                    gloamingVersion = Required("GLOAMING_PROOF_VERSION"),
                    clientPackageVersion = "2.16.0",
                    nativeClientVersion = Library.VersionString,
                    cases = Results,
                },
                PrettyJson
            ),
            Token
        );
        Console.WriteLine(
            "PASS ALL " + Results.Count + " Kafka/PostgreSQL package adoption scenarios."
        );
    }

    private static async Task ChildAsync(string[] args)
    {
        string role = args[0],
            topic = args[1],
            group = args[2];
        if (role == "publish")
        {
            await using var sender = Sender();
            using var producer = KafkaAdapter.CreateProducer(Broker);
            await new PostgresOutboxDispatcher<SenderDatabase>(
                sender,
                new PublicationBarrier(new KafkaPublisher(producer, topic)),
                new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100))
            ).DispatchNextAsync(Token);
            return;
        }

        using var consumer = Consumer(group);
        consumer.Subscribe(topic);
        var record = Next(consumer);
        await ReceiveAsync(record, role);
        if (role == "rebalance")
        {
            try
            {
                consumer.Commit(record);
                Console.WriteLine("STALE-COMMIT-SUCCEEDED");
            }
            catch (KafkaException error)
            {
                Console.WriteLine("STALE-COMMIT-REJECTED:" + error.Error.Code);
            }
        }
        else
            consumer.Commit(record);
    }

    private static IConsumer<byte[], byte[]> Consumer(string group) =>
        KafkaAdapter.CreateConsumer(Broker, group);

    private static async Task<InboxReceiveResult> ReceiveAsync(
        ConsumeResult<byte[], byte[]> record,
        string? barrier = null
    )
    {
        await using var services = Services(
            new FailureSwitch(),
            barrier is null ? null : new CommitObservation(barrier)
        );
        return await KafkaAdapter.ReceiveAsync(
            record,
            services.GetRequiredService<IServiceScopeFactory>(),
            Token
        );
    }

    private static async Task QuarantineAsync(ConsumeResult<byte[], byte[]> record, string reason)
    {
        await using var services = Services(new FailureSwitch());
        await KafkaAdapter.QuarantineAsync(
            record,
            reason,
            SourceIncarnation,
            services.GetRequiredService<IServiceScopeFactory>(),
            Token
        );
    }

    private static ConsumeResult<byte[], byte[]> Next(IConsumer<byte[], byte[]> consumer)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(40));
        return consumer.Consume(bounded.Token);
    }

    private static long Committed(string topic, string group)
    {
        using var observer = Consumer(group);
        return observer
            .Committed([new TopicPartition(topic, 0)], TimeSpan.FromSeconds(10))
            .Single()
            .Offset.Value;
    }

    private static OutgoingMessage Message(string topic, int amount) =>
        new(
            Guid.NewGuid(),
            topic,
            "example.changed",
            1,
            JsonSerializer.SerializeToElement(new { item = Guid.NewGuid(), amount }),
            "fixture-tenant",
            "probe-conversation",
            "probe-cause",
            TraceParent,
            TraceState
        );

    private static Headers ExampleHeaders() =>
        new()
        {
            { "example-tag", [1, 2, 3] },
            { "empty-tag", null },
            { "example-tag", [4] },
        };

    private static async Task<long> PublishAsync(string topic, byte[] bytes)
    {
        using var producer = KafkaAdapter.CreateProducer(Broker);
        var result = await producer.ProduceAsync(
            topic,
            new Message<byte[], byte[]>
            {
                Key = "fixture"u8.ToArray(),
                Value = bytes,
                Timestamp = RecordTimestamp,
                Headers = ExampleHeaders(),
            },
            Token
        );
        Check(
            result.Status == PersistenceStatus.Persisted,
            "Native publish must confirm acceptance."
        );
        return result.Offset.Value;
    }

    private static async Task<InboxProcessingResult> ProcessAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<ReceiverDatabase>>()
            .ProcessNextAsync(KafkaAdapter.Subscription, Token);
    }

    private static async Task CountsAsync(int inbox, int effects, int quarantine)
    {
        await using var observer = Receiver();
        Check(
            await observer.Set<InboxMessageRecord>().CountAsync(Token) == inbox,
            "Unexpected committed inbox count."
        );
        Check(
            await observer.Set<EffectRow>().CountAsync(Token) == effects,
            "Unexpected committed effect count."
        );
        Check(
            await observer.Set<QuarantineRow>().CountAsync(Token) == quarantine,
            "Unexpected committed quarantine count."
        );
    }

    private static async Task ExpectedEffectAsync(OutgoingMessage message, int amount)
    {
        await using var observer = Receiver();
        var id = message.Payload.GetProperty("item").GetGuid();
        var effect = await observer.Set<EffectRow>().SingleAsync(row => row.Id == id, Token);
        Check(
            effect.Amount == amount && effect.MessageId == message.MessageId,
            "Independent effect values must match the original accepted message."
        );
    }

    private static void Pass(string name, object evidence)
    {
        Results.Add(
            new
            {
                name,
                result = "pass",
                evidence,
            }
        );
        Console.WriteLine("PASS " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
