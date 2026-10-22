using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Messaging.Exports;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Messaging.Tests.Processes;

// Sequential process proofs share only the infrastructure fixtures. Each uses new module
// databases and a private durable queue; there is no shared module state or global DbContext.
public sealed class WorkerProcessTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task StartupRequiresExplicitSetupAndNeverSeedsBusinessWork()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellation = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, cancellation);
        var unconfigured = topology.Worker("process", "unconfigured");
        Assert.NotEqual(0, await unconfigured.WaitForExitAsync(cancellation));
        Assert.Contains("does not exist", unconfigured.Output, StringComparison.Ordinal);
        Assert.Equal(
            0,
            await WorkerTopology.SqlCountAsync(
                topology.Rendering,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'rendering'",
                cancellation
            )
        );

        // The setup command can be rerun; it is not the daemon's implicit startup behavior.
        await topology.SetupAsync(cancellation);
        await topology.SetupAsync(cancellation);
        var processor = await topology.StartWorkerAsync("process", "proof-setup", cancellation);
        await processor.StopAsync(cancellation);
        await topology.AssertCountsAsync(0, 0, 0, cancellation);
        await using var database = ExportDbContext.Create(topology.Exports);
        Assert.Empty(await database.Set<ExportRequest>().ToListAsync(cancellation));
        Assert.Empty(await database.Set<OutboxMessageRecord>().ToListAsync(cancellation));
    }

    [Fact]
    public async Task DeliveryAndProcessingContinueWithoutApiAndRestartInFreshProcesses()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, cancellation);
        await topology.SetupAsync(cancellation);
        var (api, client) = await topology.StartApiAsync(cancellation);
        Guid firstExport;
        using (client)
            firstExport = await WorkerTopology.SubmitAsync(client, 12, cancellation);

        await api.StopAsync(cancellation);
        await topology.AssertCountsAsync(0, 0, 0, cancellation);
        Guid message = await topology.MessageIdAsync(cancellation);
        var dispatcher = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch",
            cancellation
        );
        var receiver = await topology.StartWorkerAsync("receive", "proof-receive", cancellation);
        await receiver.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged",
            cancellation
        );
        await receiver.StopAsync(cancellation);
        await dispatcher.StopAsync(cancellation);

        // Intake has committed and acknowledged, but the separately hosted handler has not run.
        await topology.AssertCountsAsync(1, 0, 0, cancellation);
        await topology.AssertQueueEmptyAsync(cancellation);
        var processor = await topology.StartWorkerAsync("process", "proof-process", cancellation);
        await topology.WaitForJobsAsync(1, cancellation);
        await processor.StopAsync(cancellation);
        await topology.AssertJobAsync(firstExport, 12, cancellation);

        // Restart every role and prove new pending work progresses, rather than only reading
        // the first process's completed records. The processing host receives no broker settings.
        var (nextApi, nextClient) = await topology.StartApiAsync(cancellation);
        Guid secondExport;
        using (nextClient)
            secondExport = await WorkerTopology.SubmitAsync(nextClient, 6, cancellation);

        await nextApi.StopAsync(cancellation);
        var nextDispatch = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch-next",
            cancellation
        );
        var nextReceive = await topology.StartWorkerAsync(
            "receive",
            "proof-receive-next",
            cancellation
        );
        var nextProcess = await topology.StartWorkerAsync(
            "process",
            "proof-process-next",
            cancellation
        );
        await topology.WaitForJobsAsync(2, cancellation);
        await nextReceive.StopAsync(cancellation);
        await nextDispatch.StopAsync(cancellation);
        await nextProcess.StopAsync(cancellation);
        await topology.AssertCountsAsync(2, 2, 2, cancellation);
        await topology.AssertJobAsync(secondExport, 6, cancellation);
    }

    [Fact]
    public async Task CompetingProcessingHostsLockDifferentInboxRows()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, cancellation);
        await topology.SetupAsync(cancellation);
        var (api, client) = await topology.StartApiAsync(cancellation);
        using (client)
        {
            await WorkerTopology.SubmitAsync(client, 2, cancellation);
            await WorkerTopology.SubmitAsync(client, 3, cancellation);
        }

        await api.StopAsync(cancellation);
        var dispatcher = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch",
            cancellation
        );
        var receiver = await topology.StartWorkerAsync("receive", "proof-receive", cancellation);
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Rendering,
                    "SELECT count(*) FROM rendering.incoming_work",
                    cancellation
                ) == 2,
            cancellation
        );
        await receiver.StopAsync(cancellation);
        await dispatcher.StopAsync(cancellation);

        await using var barrier = await DatabaseBarrier.HoldAsync(
            topology.Rendering,
            publicationCompletion: false,
            cancellation
        );
        var first = await topology.StartWorkerAsync("process", "proof-first", cancellation);
        var second = await topology.StartWorkerAsync("process", "proof-second", cancellation);
        // Both stage a job INSERT while holding distinct inbox rows. Waiting on the same
        // inbox row instead of SKIP LOCKED would leave only one advisory-lock waiter here.
        await barrier.WaitForBlockedAsync(2, cancellation);
        await topology.AssertCountsAsync(2, 0, 0, cancellation);
        await barrier.ReleaseAsync(cancellation);
        await topology.WaitForJobsAsync(2, cancellation);
        await first.StopAsync(cancellation);
        await second.StopAsync(cancellation);
        await topology.AssertCountsAsync(2, 2, 2, cancellation);
    }

    [Fact]
    public async Task KilledProcessorRollsBackItsLockedDeliveryAndReplacementCompletes()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, cancellation);
        await topology.SetupAsync(cancellation);
        var (api, client) = await topology.StartApiAsync(cancellation);
        using (client)
            await WorkerTopology.SubmitAsync(client, 7, cancellation);

        await api.StopAsync(cancellation);
        Guid message = await topology.MessageIdAsync(cancellation);
        var dispatcher = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch",
            cancellation
        );
        var receiver = await topology.StartWorkerAsync("receive", "proof-receive", cancellation);
        await receiver.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged",
            cancellation
        );
        await receiver.StopAsync(cancellation);
        await dispatcher.StopAsync(cancellation);

        await using var barrier = await DatabaseBarrier.HoldAsync(
            topology.Rendering,
            publicationCompletion: false,
            cancellation
        );
        var original = await topology.StartWorkerAsync("process", "proof-killed", cancellation);
        await barrier.WaitForBlockedAsync(1, cancellation);
        await original.KillAsync(cancellation);
        // PostgreSQL may notice the disconnect only after the blocked statement can resume.
        await barrier.ReleaseAsync(cancellation);
        await WorkerTopology.WaitForBackendExitAsync(
            topology.Rendering,
            "proof-killed",
            cancellation
        );
        await topology.AssertCountsAsync(1, 0, 0, cancellation);

        var replacement = await topology.StartWorkerAsync(
            "process",
            "proof-replacement",
            cancellation
        );
        await topology.WaitForJobsAsync(1, cancellation);
        await replacement.StopAsync(cancellation);
        await topology.AssertCountsAsync(1, 1, 1, cancellation);
    }

    [Fact]
    public async Task GracefulStopCancelsInFlightProcessingAndReplacementRecovers()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, cancellation);
        await topology.SetupAsync(cancellation);
        var (api, client) = await topology.StartApiAsync(cancellation);
        using (client)
            await WorkerTopology.SubmitAsync(client, 5, cancellation);

        await api.StopAsync(cancellation);
        Guid message = await topology.MessageIdAsync(cancellation);
        var dispatcher = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch",
            cancellation
        );
        var receiver = await topology.StartWorkerAsync("receive", "proof-receive", cancellation);
        await receiver.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged",
            cancellation
        );
        await receiver.StopAsync(cancellation);
        await dispatcher.StopAsync(cancellation);

        await using var barrier = await DatabaseBarrier.HoldAsync(
            topology.Rendering,
            publicationCompletion: false,
            cancellation
        );
        var original = await topology.StartWorkerAsync("process", "proof-stopping", cancellation);
        await barrier.WaitForBlockedAsync(1, cancellation);

        // Keep the barrier closed: SIGTERM must cancel the actual statement and roll back
        // its already inserted, uncommitted job instead of waiting for maintenance to unblock it.
        await original.StopAsync(cancellation);
        await WorkerTopology.WaitForBackendExitAsync(
            topology.Rendering,
            "proof-stopping",
            cancellation
        );
        await topology.AssertCountsAsync(1, 0, 0, cancellation);
        await barrier.ReleaseAsync(cancellation);

        var replacement = await topology.StartWorkerAsync(
            "process",
            "proof-replacement",
            cancellation
        );
        await topology.WaitForJobsAsync(1, cancellation);
        await replacement.StopAsync(cancellation);
        await topology.AssertCountsAsync(1, 1, 1, cancellation);
    }

    [Fact]
    public async Task KilledConfirmedPublisherLeavesLeaseAndRedeliveryIsDeduplicated()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, cancellation);
        await topology.SetupAsync(cancellation);
        var (api, client) = await topology.StartApiAsync(cancellation);
        using (client)
            await WorkerTopology.SubmitAsync(client, 9, cancellation);

        await api.StopAsync(cancellation);
        Guid message = await topology.MessageIdAsync(cancellation);
        await using var barrier = await DatabaseBarrier.HoldAsync(
            topology.Exports,
            publicationCompletion: true,
            cancellation
        );
        var original = await topology.StartWorkerAsync(
            "dispatch",
            "proof-killed",
            cancellation,
            leaseSeconds: 3
        );
        await original.WaitForOutputAsync($"Publication {message} confirmed", cancellation);
        await barrier.WaitForBlockedAsync(1, cancellation);
        var receiver = await topology.StartWorkerAsync("receive", "proof-receive", cancellation);
        var processor = await topology.StartWorkerAsync("process", "proof-process", cancellation);
        await receiver.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged: Queued",
            cancellation
        );
        await topology.WaitForJobsAsync(1, cancellation);

        await original.KillAsync(cancellation);
        await barrier.ReleaseAsync(cancellation);
        await WorkerTopology.WaitForBackendExitAsync(
            topology.Exports,
            "proof-killed",
            cancellation
        );
        await using (var database = ExportDbContext.Create(topology.Exports))
        {
            var row = await database.Set<OutboxMessageRecord>().SingleAsync(cancellation);
            Assert.Null(row.DispatchedAt);
            Assert.NotNull(row.LeaseToken);
            Assert.Equal(1, row.Attempts);
        }

        // Wait for the real database clock; never rewrite the lease to force eligibility.
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE lease_until <= clock_timestamp()",
                    cancellation
                ) == 1,
            cancellation
        );
        var replacement = await topology.StartWorkerAsync(
            "dispatch",
            "proof-replacement",
            cancellation
        );
        await receiver.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged: AlreadyReceived",
            cancellation
        );
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE dispatched_at IS NOT NULL AND attempts = 2",
                    cancellation
                ) == 1,
            cancellation
        );
        await replacement.StopAsync(cancellation);
        await receiver.StopAsync(cancellation);
        await processor.StopAsync(cancellation);
        await topology.AssertCountsAsync(1, 1, 1, cancellation);
        await topology.AssertQueueEmptyAsync(cancellation);
    }
}
