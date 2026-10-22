using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Messaging.Tests.Rendering;

[Collection("Rendering PostgreSQL")]
public sealed class FailureSupervisionTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("policy", BackgroundServiceExceptionBehavior.StopHost)]
    [InlineData("sql", BackgroundServiceExceptionBehavior.StopHost)]
    [InlineData("policy", BackgroundServiceExceptionBehavior.Ignore)]
    public async Task AccountingFaultUsesConsumerHostSupervisionWithoutRestartingTheWorker(
        string fault,
        BackgroundServiceExceptionBehavior supervision
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var database = RenderDbContext.Create(connection))
            await database.Database.MigrateAsync(Token);

        await AdoptionTests.SqlAsync(
            connection,
            "ALTER TABLE rendering.jobs ADD CONSTRAINT renderer_unavailable CHECK (false) NOT VALID"
        );
        if (fault == "sql")
            await AdoptionTests.SqlAsync(
                connection,
                "ALTER TABLE rendering.incoming_work ADD CONSTRAINT reject_accounting CHECK (failure_count = 0)"
            );

        var builder = Host.CreateApplicationBuilder();
        InboxTestHost.Register(builder.Services, connection);
        if (fault == "policy")
        {
            builder.Services.Remove(
                Assert.Single(
                    builder.Services,
                    item => item.ServiceType == typeof(IInboxFailurePolicy<RenderDbContext>)
                )
            );
            builder.Services.AddInboxFailurePolicy<RenderDbContext, ThrowingPolicy>(
                RenderExportHandler.Subscription
            );
        }

        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = supervision
        );
        builder.Services.AddInboxWorker<RenderDbContext>(
            RenderExportHandler.Subscription,
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        using var host = builder.Build();
        Guid messageId = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<RenderDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<RenderDbContext>>()
                .ReceiveAsync(
                    RenderExportHandler.Subscription,
                    new IncomingMessage(
                        messageId,
                        "exports",
                        "exports.render",
                        1,
                        JsonSerializer.SerializeToElement(
                            new { ExportRequestId = Guid.NewGuid(), Pages = 2 }
                        )
                    ),
                    Token
                );
            await transaction.CommitAsync(Token);
        }

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStopping.Register(() =>
            stopping.TrySetResult()
        );
        var worker = Assert.Single(
            host.Services.GetServices<IHostedService>().OfType<BackgroundService>()
        );
        await host.StartAsync(Token);
        try
        {
            var failure = await Assert.ThrowsAsync<InboxFailureAccountingException>(() =>
                worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            Assert.Equal(messageId, failure.MessageId);
            Assert.IsType<DbUpdateException>(failure.ProcessingFailure);
            if (supervision == BackgroundServiceExceptionBehavior.StopHost)
            {
                await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
                await host.WaitForShutdownAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
            }
            else
            {
                Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
                Assert.True(worker.ExecuteTask!.IsFaulted);
            }
        }
        finally
        {
            await host.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
        }

        await using var read = RenderDbContext.Create(connection);
        Assert.Empty(await read.Set<RenderJob>().ToArrayAsync(Token));
        var row = await read.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(0, row.FailureCount);
        Assert.Equal(0, row.Revision);
        Assert.Null(row.ProcessedAt);
        Assert.Null(row.DeadLetteredAt);
    }

    public sealed class ThrowingPolicy : IInboxFailurePolicy<RenderDbContext>
    {
        public InboxFailureDecision Decide(InboxFailure failure) =>
            throw new InvalidOperationException("classification unavailable");
    }
}
