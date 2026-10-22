using Gloaming.ActorIdentity;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.ServiceDefaults;
using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ModulithFoundry.Samples.Wholesale.Worker.Broker;

internal static class BrokerHost
{
    internal static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        string role = builder.Configuration["role"] ?? "run";
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (role == "setup-sales-replies")
        {
            await SalesReplyDatabase.ProvisionAsync(
                Required("ConnectionStrings:SalesRepliesSetup"),
                Required("ConnectionStrings:SalesReplies"),
                startup.Token
            );
            Console.WriteLine("Sales reply role, policies and grants ready.");
            return;
        }

        if (role == "process-sales-replies")
        {
            foreach (
                string key in new[]
                {
                    "ConnectionStrings:SalesRepliesSetup",
                    "ConnectionStrings:SalesDispatchSetup",
                    "ConnectionStrings:SalesDispatch",
                    "ConnectionStrings:Sales",
                    "ConnectionStrings:Inventory",
                    "ConnectionStrings:Access",
                    "ConnectionStrings:Setup",
                    "ConnectionStrings:SalesMigrations",
                    "ConnectionStrings:RabbitMq",
                    "Parameters:postgres-password",
                }
            )
                if (!string.IsNullOrEmpty(builder.Configuration[key]))
                    throw new InvalidOperationException(
                        "Keep provisioning, alternate database and broker credentials out of the Sales reply host."
                    );

            string runtime = Required("ConnectionStrings:SalesReplies");
            builder.Services.AddSalesReservationReplyProcessing(runtime, Required("Organization"));
            await SalesReplyDatabase.VerifyAsync(runtime, startup.Token);
            builder.Services.AddInboxWorker<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
            );
            builder.AddServiceDefaults();
            builder
                .Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddSource("Gloaming.Messaging"))
                .WithMetrics(metrics => metrics.AddMeter("Gloaming.Messaging"));
            using var replyHost = builder.Build();
            await replyHost.RunAsync();
            return;
        }

        if (role == "setup-sales-dispatch")
        {
            await SalesDispatchDatabase.ProvisionAsync(
                Required("ConnectionStrings:SalesDispatchSetup"),
                Required("ConnectionStrings:SalesDispatch"),
                startup.Token
            );
            Console.WriteLine("Sales dispatch role and grants ready.");
            return;
        }

        if (role is "dispatch-sales" or "inspect-sales-outbox")
        {
            foreach (
                string key in new[]
                {
                    "ConnectionStrings:SalesDispatchSetup",
                    "ConnectionStrings:SalesReplies",
                    "ConnectionStrings:SalesRepliesSetup",
                    "ConnectionStrings:Sales",
                    "ConnectionStrings:Inventory",
                    "ConnectionStrings:Access",
                    "ConnectionStrings:Setup",
                    "ConnectionStrings:SalesMigrations",
                    "Parameters:postgres-password",
                }
            )
                if (!string.IsNullOrEmpty(builder.Configuration[key]))
                    throw new InvalidOperationException(
                        "Keep provisioning and business database credentials out of the Sales dispatch host."
                    );

            string runtime = Required("ConnectionStrings:SalesDispatch");
            await SalesDispatchDatabase.VerifyAsync(runtime, startup.Token);
            if (role == "inspect-sales-outbox")
            {
                await using var database = new SalesOutboxDbContext(
                    new DbContextOptionsBuilder<SalesOutboxDbContext>().UseNpgsql(runtime).Options
                );
                foreach (
                    string route in new[]
                    {
                        "inventory.issue-stock",
                        "inventory.reserve-stock",
                        "inventory.release-reservation",
                    }
                )
                {
                    var backlog = await database.ReadPostgresOutboxBacklogAsync(
                        route,
                        startup.Token
                    );
                    Console.WriteLine(
                        System.Text.Json.JsonSerializer.Serialize(
                            new { RouteKey = route, Backlog = backlog }
                        )
                    );
                }

                return;
            }

            await using var dispatchBroker = await WorkflowBroker.OpenAsync(
                Required("ConnectionStrings:RabbitMq"),
                builder.Configuration["QueuePrefix"] ?? "wf1",
                startup.Token
            );
            await dispatchBroker.RequireQueuesAsync(startup.Token);
            builder.Services.AddSingleton(dispatchBroker);
            builder.Services.AddDbContext<SalesOutboxDbContext>(options =>
                options.UseNpgsql(runtime)
            );
            builder.Services.AddPostgresOutboxDispatcher<
                SalesOutboxDbContext,
                SalesCommandPublisher
            >(SalesDispatchOptions());
            builder.Services.AddOutboxWorker<SalesOutboxDbContext>(
                new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
            );
            builder.AddServiceDefaults();
            builder
                .Services.AddOpenTelemetry()
                .WithTracing(tracing =>
                    tracing.AddSource("Gloaming.Messaging").AddSource("RabbitMQ.Client.Publisher")
                )
                .WithMetrics(metrics => metrics.AddMeter("Gloaming.Messaging"));
            using var dispatchHost = builder.Build();
            await dispatchHost.RunAsync();
            return;
        }

        string sales = Required("ConnectionStrings:Sales");
        string inventory = Required("ConnectionStrings:Inventory");
        builder.Services.AddWorkflowDemo(sales, inventory);

        if (
            role
            is "start"
                or "read"
                or "seed"
                or "reserve-order"
                or "read-order"
                or "retry-compensation"
        )
        {
            using var operations = builder.Build();
            await using var scope = operations.Services.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(
                    TenantContext.ForTenant(
                        new TenantId(builder.Configuration["Organization"] ?? "wholesale-alpha")
                    )
                );
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.System(new ActorId("sales.workflow-demo"))));
            var requests = scope.ServiceProvider.GetRequiredService<IStockIssueRequests>();
            if (role is "reserve-order" or "read-order" or "retry-compensation")
            {
                var orders = scope.ServiceProvider.GetRequiredService<IStockReservationOrders>();
                Guid id = Guid.Parse(Required("OrderId"));
                var order = role switch
                {
                    "read-order" => await orders.ReadAsync(id, startup.Token),
                    "retry-compensation" => await orders.RetryCompensationAsync(id, startup.Token),
                    _ => await orders.StartAsync(
                        new(
                            id,
                            new(
                                Guid.Parse(Required("FirstStockId")),
                                builder.Configuration.GetValue("FirstVersion", 2L),
                                builder.Configuration.GetValue("FirstQuantity", 1m)
                            ),
                            new(
                                Guid.Parse(Required("SecondStockId")),
                                builder.Configuration.GetValue("SecondVersion", 2L),
                                builder.Configuration.GetValue("SecondQuantity", 1m)
                            )
                        ),
                        startup.Token
                    ),
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(order));
            }
            else if (role == "seed")
            {
                var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                await using var transaction = await database.Database.BeginTransactionAsync(
                    startup.Token
                );
                var commands = scope.ServiceProvider.GetRequiredService<IStockPositionCommands>();
                Guid stock = Guid.Parse(Required("StockId"));
                var opened = await commands.OpenAsync(
                    new(stock, Guid.NewGuid(), Guid.NewGuid(), "EA"),
                    startup.Token
                );
                if (opened is not StockPositionChangeResult.Changed)
                    throw new InvalidOperationException("Seed needs a new stock identity.");

                await database.SaveChangesAsync(startup.Token);
                await transaction.CommitAsync(startup.Token);
                await using var receiptScope = operations.Services.CreateAsyncScope();
                receiptScope
                    .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                    .Initialize(
                        TenantContext.ForTenant(
                            new TenantId(builder.Configuration["Organization"] ?? "wholesale-alpha")
                        )
                    );
                var receiptDatabase =
                    receiptScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                await using var receipt = await receiptDatabase.Database.BeginTransactionAsync(
                    startup.Token
                );
                var received = await receiptScope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .ReceiveAsync(new(stock, 1, [new StockReceipt(5)]), startup.Token);
                if (received is not StockPositionChangeResult.Changed)
                    throw new InvalidOperationException("The seed receipt did not change stock.");

                await receiptDatabase.SaveChangesAsync(startup.Token);
                await receipt.CommitAsync(startup.Token);
                Console.WriteLine("Stock seeded at version 2 with quantity 5.");
            }
            else
            {
                Guid request = Guid.Parse(Required("RequestId"));
                var progress =
                    role == "read"
                        ? await requests.ReadAsync(request, startup.Token)
                        : await requests.StartAsync(
                            new(
                                request,
                                Guid.Parse(Required("StockId")),
                                builder.Configuration.GetValue("ExpectedVersion", 2L),
                                builder.Configuration.GetValue("Quantity", 1m),
                                DateTimeOffset.Parse(
                                    Required("ReplyDeadline"),
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                            ),
                            startup.Token
                        );
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(progress));
            }

            return;
        }

        await using var broker = await WorkflowBroker.OpenAsync(
            Required("ConnectionStrings:RabbitMq"),
            builder.Configuration["QueuePrefix"] ?? "wf1",
            startup.Token
        );
        builder.Services.AddSingleton(broker);
        if (role == "setup")
        {
            using var setup = builder.Build();
            await using var scope = setup.Services.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.MigrateAsync(startup.Token);
            await scope
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(startup.Token);
            await broker.SetupAsync(startup.Token);
            Console.WriteLine("Workflow schemas and queues ready.");
            return;
        }

        if (role != "run")
            throw new InvalidOperationException(
                "Choose --role setup, seed, start, read, reserve-order, read-order, retry-compensation or run."
            );

        // Fail before hosting if explicit setup has not installed the required storage/topology.
        // These privileged probes check structure only; they do not expose tenant business data.
        await broker.RequireQueuesAsync(startup.Token);
        builder.AddServiceDefaults();
        builder
            .Services.AddOpenTelemetry()
            .WithTracing(tracing =>
                tracing
                    .AddSource("Gloaming.Messaging")
                    .AddSource("RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber")
            )
            .WithMetrics(metrics => metrics.AddMeter("Gloaming.Messaging"));
        builder.Services.AddPostgresOutboxDispatcher<SalesDbContext, SalesCommandPublisher>(
            SalesDispatchOptions()
        );
        builder.Services.AddPostgresOutboxDispatcher<InventoryDbContext, InventoryReplyPublisher>(
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddOutboxWorker<SalesDbContext>(
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddOutboxWorker<InventoryDbContext>(
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddInboxWorker<SalesDbContext>(
            StockIssueReplyAdmission.Subscription,
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddInboxWorker<InventoryDbContext>(
            StockIssueMessageAdmission.Subscription,
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddInboxWorker<SalesDbContext>(
            StockReservationReplyAdmission.Subscription,
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddInboxWorker<InventoryDbContext>(
            StockReservationAdmission.Subscription,
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1))
        );
        builder.Services.AddHostedService<WorkflowIntakeWorker>();
        builder.Services.AddHostedService<StockIssueDeadlineWorker>();
        using var host = builder.Build();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var salesDatabase = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            var inventoryDatabase = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            _ = await salesDatabase
                .Set<InboxMessageRecord>()
                .IgnoreQueryFilters()
                .AnyAsync(startup.Token);
            _ = await salesDatabase
                .Set<OutboxMessageRecord>()
                .IgnoreQueryFilters()
                .AnyAsync(startup.Token);
            _ = await inventoryDatabase
                .Set<InboxMessageRecord>()
                .IgnoreQueryFilters()
                .AnyAsync(startup.Token);
            _ = await inventoryDatabase
                .Set<OutboxMessageRecord>()
                .IgnoreQueryFilters()
                .AnyAsync(startup.Token);
            _ = await salesDatabase
                .Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM sales.stock_issue_requests")
                .Take(1)
                .ToArrayAsync(startup.Token);
            _ = await salesDatabase
                .Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM sales.workflow_instances")
                .Take(1)
                .ToArrayAsync(startup.Token);
            _ = await inventoryDatabase
                .Database.SqlQueryRaw<int>(
                    "SELECT 1 AS \"Value\" FROM inventory.stock_reservations"
                )
                .Take(1)
                .ToArrayAsync(startup.Token);
        }

        await host.RunAsync();

        string Required(string key) =>
            builder.Configuration[key]
            ?? throw new InvalidOperationException("Configure " + key + ".");
    }

    private static OutboxDispatchOptions SalesDispatchOptions() =>
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1))
        {
            RouteKeys =
            [
                "inventory.issue-stock",
                "inventory.reserve-stock",
                "inventory.release-reservation",
            ],
        };
}
