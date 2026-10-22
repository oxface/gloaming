using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.InboxDemo;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.MessagingWorkerDemo.Tests;

public sealed class BrokerRecoveryTests(PostgreSqlFixture postgres, BrokerRecoveryFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<BrokerRecoveryFixture>
{
    [Fact]
    public async Task ConfirmationTimeoutAccountsFailureAndSameOpenSessionProgressesAfterAlarmClears()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(
            postgres,
            rabbit.ConnectionString,
            token
        );
        await topology.SetupAsync(token);
        var (api, client) = await topology.StartApiAsync(token);
        Guid export;
        using (client)
            export = await WorkerTopology.SubmitAsync(client, 8, token);
        await api.StopAsync(token);
        Guid message = await topology.MessageIdAsync(token);
        ChildHost dispatch;
        try
        {
            await rabbit.BlockPublicationAsync(token);
            dispatch = await topology.StartWorkerAsync("dispatch", "proof-timeout-dispatch", token);
            await dispatch.WaitForOutputAsync("Broker connection blocked:", token);
            await dispatch.WaitForOutputAsync($"Message {message} dispatch attempt failed", token);
            await using var database = ExportDbContext.Create(topology.Exports);
            var failed = await database.Set<OutboxMessageRecord>().SingleAsync(token);
            Assert.True(failed.FailureCount >= 1);
            Assert.Null(failed.DispatchedAt);
            Assert.Null(failed.DeadLetteredAt);
            Assert.DoesNotContain("Broker session lost", dispatch.Output, StringComparison.Ordinal);
        }
        finally
        {
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await rabbit.UnblockPublicationAsync(restore.Token);
        }

        // No replacement: the ordinary confirmation timeout must not destroy this session.
        await dispatch.WaitForOutputAsync("Broker connection unblocked.", token);
        await dispatch.WaitForOutputAsync($"Publication {message} confirmed", token);
        var intake = await topology.StartWorkerAsync("receive", "proof-timeout-intake", token);
        var process = await topology.StartWorkerAsync("process", "proof-timeout-process", token);
        await topology.WaitForJobsAsync(1, token);
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE dispatched_at IS NOT NULL",
                    token
                ) == 1,
            token
        );
        await dispatch.StopAsync(token);
        await WorkerTopology.EventuallyAsync(
            async () => await rabbit.MessageCountAsync(topology.Queue, token) == 0,
            token
        );
        await intake.StopAsync(token);
        await process.StopAsync(token);
        await topology.AssertCountsAsync(1, 1, 1, token);
        await topology.AssertJobAsync(export, 8, token);
        await topology.AssertQueueEmptyAsync(token);
        await using var outgoing = ExportDbContext.Create(topology.Exports);
        var completed = await outgoing.Set<OutboxMessageRecord>().SingleAsync(token);
        Assert.Equal(message, completed.MessageId);
        Assert.True(completed.Attempts >= 2);
        Assert.True(completed.FailureCount >= 1);
        Assert.DoesNotContain("Broker session lost", dispatch.Output, StringComparison.Ordinal);
        await using var incoming = RenderDbContext.Create(topology.Rendering);
        Assert.Equal(
            message,
            (await incoming.Set<InboxMessageRecord>().SingleAsync(token)).MessageId
        );
    }

    [Fact]
    public async Task SigtermDuringBrokerBlockedPublicationRetainsClaimAndReplacementProgressesSameIdentity()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(
            postgres,
            rabbit.ConnectionString,
            token
        );
        await topology.SetupAsync(token);
        var (api, client) = await topology.StartApiAsync(token);
        Guid export;
        using (client)
            export = await WorkerTopology.SubmitAsync(client, 6, token);
        await api.StopAsync(token);

        OutboxMessageRecord before;
        await using (var database = ExportDbContext.Create(topology.Exports))
            before = await database.Set<OutboxMessageRecord>().SingleAsync(token);

        try
        {
            await rabbit.BlockPublicationAsync(token);
            var dispatch = await topology.StartWorkerAsync(
                "dispatch",
                "proof-blocked-dispatch",
                token,
                leaseSeconds: 3
            );
            using var phase = CancellationTokenSource.CreateLinkedTokenSource(token);
            phase.CancelAfter(TimeSpan.FromSeconds(15));
            await dispatch.WaitForOutputAsync(
                $"Publication {before.MessageId} started",
                phase.Token
            );
            await dispatch.WaitForOutputAsync("Broker connection blocked:", phase.Token);
            await using (var database = ExportDbContext.Create(topology.Exports))
            {
                var claimed = await database.Set<OutboxMessageRecord>().SingleAsync(token);
                Assert.Equal(1, claimed.Attempts);
                Assert.NotNull(claimed.LeaseToken);
                Assert.Null(claimed.DispatchedAt);
            }

            // Keep the alarm raised throughout SIGTERM. Neither unblocking the broker nor
            // an arbitrary timer is allowed to establish successful process cancellation.
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await dispatch.StopAsync(stop.Token);
            Assert.DoesNotContain("Broker session lost", dispatch.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "dispatch attempt failed",
                dispatch.Output,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                $"Publication {before.MessageId} confirmed",
                dispatch.Output,
                StringComparison.Ordinal
            );
            await using var read = ExportDbContext.Create(topology.Exports);
            var abandoned = await read.Set<OutboxMessageRecord>().SingleAsync(token);
            Assert.Null(abandoned.DispatchedAt);
            Assert.NotNull(abandoned.LeaseToken);
            Assert.Equal(1, abandoned.Attempts);
            Assert.Equal(1, abandoned.Revision);
            Assert.Equal(0, abandoned.FailureCount);
        }
        finally
        {
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await rabbit.UnblockPublicationAsync(restore.Token);
        }

        // Cancellation does not establish rejection: the broker may release an already
        // buffered publication. Retained intake must deduplicate any acceptance/retry.
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE lease_until <= clock_timestamp()",
                    token
                ) == 1,
            token
        );
        var intake = await topology.StartWorkerAsync("receive", "proof-unblocked-intake", token);
        var replacement = await topology.StartWorkerAsync(
            "dispatch",
            "proof-unblocked-dispatch",
            token
        );
        var process = await topology.StartWorkerAsync("process", "proof-unblocked-process", token);
        await topology.WaitForJobsAsync(1, token);
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE dispatched_at IS NOT NULL",
                    token
                ) == 1,
            token
        );
        await replacement.StopAsync(token);
        // Native total includes unacknowledged deliveries, so a temporarily empty ready
        // queue cannot hide intake still executing before cancellation/requeue.
        await WorkerTopology.EventuallyAsync(
            async () => await rabbit.MessageCountAsync(topology.Queue, token) == 0,
            token
        );
        await intake.StopAsync(token);
        await process.StopAsync(token);
        await topology.AssertCountsAsync(1, 1, 1, token);
        await topology.AssertQueueEmptyAsync(token);
        await topology.AssertJobAsync(export, 6, token);
        await using var outgoing = ExportDbContext.Create(topology.Exports);
        var completed = await outgoing.Set<OutboxMessageRecord>().SingleAsync(token);
        Assert.Equal(before.MessageId, completed.MessageId);
        Assert.Equal(before.SchemaVersion, completed.SchemaVersion);
        Assert.Equal(before.Payload.GetRawText(), completed.Payload.GetRawText());
        Assert.Equal(before.CorrelationId, completed.CorrelationId);
        Assert.Equal(before.CausationId, completed.CausationId);
        Assert.Equal(2, completed.Attempts);
        Assert.Equal(0, completed.FailureCount);
        Assert.Equal(3, completed.Revision);
        Assert.Null(completed.LeaseToken);
        await using var incoming = RenderDbContext.Create(topology.Rendering);
        Assert.Equal(
            before.MessageId,
            (await incoming.Set<InboxMessageRecord>().SingleAsync(token)).MessageId
        );
    }

    [Fact]
    public async Task LiveBrokerLossStopsIdleDispatchAndBlockedIntakeThenFreshRolesRecoverRetainedWork()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(
            postgres,
            rabbit.ConnectionString,
            token
        );
        await topology.SetupAsync(token);
        var (api, client) = await topology.StartApiAsync(token);
        using var ownedClient = client;
        Guid firstExport = await WorkerTopology.SubmitAsync(client, 4, token);
        Guid firstMessage = await topology.MessageIdAsync(token);
        var dispatch = await topology.StartWorkerAsync("dispatch", "proof-outage-dispatch", token);
        await dispatch.WaitForOutputAsync($"Publication {firstMessage} confirmed", token);
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE dispatched_at IS NOT NULL",
                    token
                ) == 1,
            token
        );

        await using var barrier = await DatabaseBarrier.HoldIntakeAsync(topology.Rendering, token);
        var intake = await topology.StartWorkerAsync("receive", "proof-outage-intake", token);
        await barrier.WaitForBlockedAsync(1, token);
        await topology.AssertCountsAsync(0, 0, 0, token);
        Guid secondExport;
        try
        {
            await rabbit.StopApplicationAsync(token);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Assert.Equal(1, await dispatch.WaitForExitAsync(stop.Token));
            Assert.Equal(1, await intake.WaitForExitAsync(stop.Token));
            Assert.Contains("Broker session lost", dispatch.Output, StringComparison.Ordinal);
            Assert.Contains("Broker session lost", intake.Output, StringComparison.Ordinal);
            await WorkerTopology.WaitForBackendExitAsync(
                topology.Rendering,
                "proof-outage-intake",
                token
            );
            await topology.AssertCountsAsync(0, 0, 0, token);

            // The producer owns only PostgreSQL; commits continue while RabbitMQ is down.
            secondExport = await WorkerTopology.SubmitAsync(client, 7, token);
            var unavailable = topology.Worker("dispatch", "proof-unavailable");
            Assert.NotEqual(0, await unavailable.WaitForExitAsync(token));
            Assert.DoesNotContain(
                "Application started.",
                unavailable.Output,
                StringComparison.Ordinal
            );
            Assert.Equal(
                1,
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE dispatched_at IS NULL AND attempts = 0",
                    token
                )
            );
        }
        finally
        {
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await rabbit.StartApplicationAsync(restore.Token);
            await barrier.ReleaseAsync(restore.Token);
        }

        var recoveredIntake = await topology.StartWorkerAsync(
            "receive",
            "proof-recovered-intake",
            token
        );
        await recoveredIntake.WaitForOutputAsync(
            $"Delivery {firstMessage} retained and acknowledged: Queued",
            token
        );
        var recoveredDispatch = await topology.StartWorkerAsync(
            "dispatch",
            "proof-recovered-dispatch",
            token
        );
        var process = await topology.StartWorkerAsync("process", "proof-recovered-process", token);
        await topology.WaitForJobsAsync(2, token);
        await recoveredDispatch.StopAsync(token);
        await recoveredIntake.StopAsync(token);
        await process.StopAsync(token);
        await api.StopAsync(token);
        await topology.AssertCountsAsync(2, 2, 2, token);
        await topology.AssertJobAsync(firstExport, 4, token);
        await topology.AssertJobAsync(secondExport, 7, token);
        await topology.AssertQueueEmptyAsync(token);
        await using var outgoing = ExportDbContext.Create(topology.Exports);
        await using var incoming = RenderDbContext.Create(topology.Rendering);
        Assert.Equal(
            (
                await outgoing
                    .Set<OutboxMessageRecord>()
                    .Select(row => row.MessageId)
                    .ToListAsync(token)
            ).Order(),
            (
                await incoming
                    .Set<InboxMessageRecord>()
                    .Select(row => row.MessageId)
                    .ToListAsync(token)
            ).Order()
        );
        Assert.DoesNotContain(
            "Broker session lost",
            recoveredDispatch.Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Broker session lost",
            recoveredIntake.Output,
            StringComparison.Ordinal
        );
    }
}
