using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Broker;

internal sealed class SalesReplyProof(WorkflowProof workflow, string runtime) : IAsyncDisposable
{
    internal WorkflowProof Workflow => workflow;
    internal ReservationProof Reservations { get; } = new(workflow);
    internal string Runtime => runtime;

    internal Dictionary<string, string> Environment(string organization = WorkflowProof.Alpha) =>
        new()
        {
            ["ConnectionStrings__SalesReplies"] = runtime,
            ["Organization"] = organization,
            ["Parameters__postgres-password"] = "",
        };

    internal static async Task<SalesReplyProof> CreateAsync(
        PostgreSqlFixture postgres,
        RabbitMqFixture rabbit,
        CancellationToken token
    )
    {
        var workflow = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        string runtime = new NpgsqlConnectionStringBuilder(workflow.SalesConnection)
        {
            Username = "replies_" + Guid.NewGuid().ToString("N"),
            Password = "replies;\"quotes'and\\slashes",
            Pooling = true,
            MaxPoolSize = 1,
            Timeout = 5,
        }.ConnectionString;
        var proof = new SalesReplyProof(workflow, runtime);
        try
        {
            await using var setup = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                new()
                {
                    ["ConnectionStrings__SalesRepliesSetup"] = workflow.SalesConnection,
                    ["ConnectionStrings__SalesReplies"] = runtime,
                },
                "--role",
                "setup-sales-replies"
            );
            Assert.True(await setup.WaitForExitAsync(token) == 0, setup.Output);
            Assert.Contains(
                "Sales reply role, policies and grants ready.",
                setup.Output,
                StringComparison.Ordinal
            );
            return proof;
        }
        catch
        {
            await proof.DisposeAsync();
            throw;
        }
    }

    internal ServiceProvider Services(
        string organization = WorkflowProof.Alpha,
        Action<IServiceCollection>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddSalesReservationReplyProcessing(runtime, organization);
        configure?.Invoke(services);
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    internal ChildHost StartWorker(
        string organization = WorkflowProof.Alpha,
        Dictionary<string, string>? environment = null
    ) =>
        new(
            typeof(WorkflowComposition).Assembly,
            environment ?? Environment(organization),
            "--role",
            "process-sales-replies"
        );

    internal static StartStockReservationOrder Request() =>
        new(Guid.NewGuid(), new(Guid.NewGuid(), 2, 1), new(Guid.NewGuid(), 2, 1));

    internal async Task<IncomingMessage> ReplyAsync(
        Guid order,
        CancellationToken token,
        string organization = WorkflowProof.Alpha
    )
    {
        await using var scope = workflow.Scope(organization);
        var row = await scope
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.CorrelationId == order.ToString("D"), token);
        var request = row.Payload.Deserialize<ReserveStockV1>(WorkflowProof.Json)!;
        return new(
            Guid.NewGuid(),
            "inventory",
            "inventory.reservation-decided",
            1,
            JsonSerializer.SerializeToElement(
                new StockReservationDecidedV1(
                    request.ReservationId,
                    request.StockPositionId,
                    request.ExpectedVersion,
                    request.Quantity,
                    StockReservationDecision.Reserved
                ),
                WorkflowProof.Json
            ),
            organization,
            order.ToString("D"),
            row.MessageId.ToString("D")
        );
    }

    internal Task IntakeAsync(IncomingMessage message, CancellationToken token) =>
        Reservations.IntakeAsync<SalesDbContext>(
            StockReservationReplyAdmission.Subscription,
            message,
            token
        );

    internal static async Task<InboxProcessingResult> ProcessAsync(
        ServiceProvider services,
        CancellationToken token
    )
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<SalesDbContext>>()
            .ProcessNextAsync(StockReservationReplyAdmission.Subscription, token);
    }

    internal Task<long> CountAsync(string sql, CancellationToken token) =>
        WorkflowProof.CountAsync(workflow.SalesConnection, sql, token);

    internal Task SqlAsync(string sql, CancellationToken token) =>
        WorkflowProof.SqlAsync(workflow.SalesConnection, sql, token);

    public async ValueTask DisposeAsync()
    {
        using var pool = new NpgsqlConnection(runtime);
        NpgsqlConnection.ClearPool(pool);
        await workflow.DisposeAsync();
    }
}
