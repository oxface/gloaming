using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using ModulithFoundry.Samples.Wholesale.Worker.Directory.Delivery;

namespace ModulithFoundry.Samples.Wholesale.Worker.Directory;

public static class DirectoryHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var action = builder.Configuration["Operation"] ?? "run";
        switch (action)
        {
            case "setup" or "populate":
                builder.Services.AddDeliveryDirectory(
                    Required("ConnectionStrings:Sales"),
                    Required("ConnectionStrings:Dispatch")
                );
                break;
            case "admit" or "process" or "search" or "progress":
                builder.Services.AddDirectoryReceiver(Required("ConnectionStrings:Dispatch"));
                break;
            case "relay" or "run":
                builder.Services.AddDirectoryReceiver(Required("ConnectionStrings:Dispatch"));
                builder.Services.AddDbContext<SalesOutboxDbContext>(options =>
                    options.UseNpgsql(Required("ConnectionStrings:SalesDispatch"))
                );
                builder.Services.AddPostgresOutboxDispatcher<
                    SalesOutboxDbContext,
                    DirectoryPublisher
                >(
                    new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50))
                    {
                        RouteKeys = [DispatchRegistration.DirectorySubscription],
                    }
                );
                break;
            case "export":
                builder.Services.AddDirectorySource(Required("ConnectionStrings:Sales"));
                break;
            default:
                throw new ArgumentException(
                    "Directory operations are setup, admit, populate, relay, process, search, progress, export or run."
                );
        }

        if (action == "run")
        {
            builder.Services.AddOutboxWorker<SalesOutboxDbContext>(
                new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            );
            builder.Services.AddInboxWorker<DispatchDbContext>(
                DispatchRegistration.DirectorySubscription,
                new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
            );
        }

        using var host = builder.Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        if (action is "relay" or "run")
            await SalesDispatchDatabase.VerifyAsync(
                Required("ConnectionStrings:SalesDispatch"),
                token
            );

        if (action == "setup")
        {
            await using var setup = host.Services.CreateAsyncScope();
            await setup
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.MigrateAsync(token);
            await setup
                .ServiceProvider.GetRequiredService<DispatchDbContext>()
                .Database.MigrateAsync(token);
            return;
        }

        if (action == "run")
        {
            await host.RunAsync();
            return;
        }

        if (action == "relay")
        {
            await using var delivery = host.Services.CreateAsyncScope();
            Print(
                await delivery
                    .ServiceProvider.GetRequiredService<IOutboxDispatcher<SalesOutboxDbContext>>()
                    .DispatchNextAsync(token)
            );
            return;
        }

        if (action == "process")
        {
            await using var delivery = host.Services.CreateAsyncScope();
            Print(
                await delivery
                    .ServiceProvider.GetRequiredService<IInboxProcessor<DispatchDbContext>>()
                    .ProcessNextAsync(DispatchRegistration.DirectorySubscription, token)
            );
            return;
        }

        var organization = Required("Organization");
        if (action == "populate")
        {
            while (!await DirectoryComposition.CopyNextAsync(host.Services, organization, token))
            { }

            // Report committed receiver progress from a fresh operation.
        }

        await using var operation = host.Services.CreateAsyncScope();
        DirectoryComposition.Establish(operation.ServiceProvider, organization);
        var services = operation.ServiceProvider;
        object? result;
        if (action is "admit" or "populate" or "search" or "progress")
        {
            var directory = services.GetRequiredService<ICustomerDirectory>();
            result = action switch
            {
                "admit" => await directory.BeginAsync(token),
                "search" => await directory.SearchAsync(builder.Configuration["Text"] ?? "", token),
                _ => await directory.ProgressAsync(token),
            };
        }
        else
        {
            var source = services.GetRequiredService<ICustomerDeliveryDirectory>();
            result = await source.ReadPageAsync(
                builder.Configuration["After"] is { } after ? Guid.Parse(after) : null,
                builder.Configuration.GetValue("Limit", 100),
                token
            );
        }

        Print(result);

        string Required(string key) =>
            builder.Configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException("Configure " + key + ".");
    }

    private static void Print<T>(T value) =>
        Console.WriteLine("DIRECTORY " + JsonSerializer.Serialize(value, Json));
}
