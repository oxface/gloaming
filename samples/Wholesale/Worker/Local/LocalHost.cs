using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Worker.Local;
using Gloaming.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gloaming.Samples.Wholesale.Worker.Local;

internal static class LocalHost
{
    internal static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        string role = builder.Configuration["role"] ?? "run";
        builder.Services.AddLocalReservationDemo(
            Required("ConnectionStrings:Sales"),
            Required("ConnectionStrings:Inventory"),
            TimeSpan.FromSeconds(builder.Configuration.GetValue("LeaseSeconds", 10))
        );
        if (role == "run")
            builder.Services.AddLocalReservationWorkers();

        using var host = builder.Build();
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (role == "setup")
        {
            await LocalWorkflowSetup.MigrateAsync(host.Services, startup.Token);
            Console.WriteLine("Local workflow schemas ready.");
            return;
        }

        if (role == "run")
        {
            await LocalWorkflowSetup.RequireStorageAsync(host.Services, startup.Token);
            await host.RunAsync();
            return;
        }

        string organization = builder.Configuration["Organization"] ?? "wholesale-alpha";
        if (role == "seed")
        {
            await LocalWorkflowSetup.SeedAsync(
                host.Services,
                Guid.Parse(Required("StockId")),
                organization,
                startup.Token
            );
            return;
        }

        if (role is not ("start" or "read" or "retry-compensation"))
            throw new InvalidOperationException(
                "Choose --role setup, seed, start, read, retry-compensation or run."
            );

        await using var operation = host.Services.CreateAsyncScope();
        operation
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        operation
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.System(new ActorId("sales.local-workflow-demo"))));
        var orders = operation.ServiceProvider.GetRequiredService<IStockReservationOrders>();
        Guid orderId = Guid.Parse(Required("OrderId"));
        var result = role switch
        {
            "read" => await orders.ReadAsync(orderId, startup.Token),
            "retry-compensation" => await orders.RetryCompensationAsync(
                orderId,
                Guid.Parse(Required("OperationId")),
                startup.Token
            ),
            _ => await orders.StartAsync(
                new(
                    orderId,
                    new(
                        Guid.Parse(Required("FirstStockId")),
                        builder.Configuration.GetValue("FirstVersion", 2L),
                        builder.Configuration.GetValue("FirstQuantity", 2m)
                    ),
                    new(
                        Guid.Parse(Required("SecondStockId")),
                        builder.Configuration.GetValue("SecondVersion", 2L),
                        builder.Configuration.GetValue("SecondQuantity", 2m)
                    )
                ),
                startup.Token
            ),
        };
        Console.WriteLine(JsonSerializer.Serialize(result));

        string Required(string key) =>
            builder.Configuration[key]
            ?? throw new InvalidOperationException("Configure " + key + ".");
    }
}
