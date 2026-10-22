using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Samples.Wholesale.DirectoryDemo;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

var builder = Host.CreateApplicationBuilder(args);
string role = builder.Configuration["role"] ?? "run";
builder.Services.AddDeliveryDirectory(
    Required("ConnectionStrings:Sales"),
    Required("ConnectionStrings:Dispatch")
);
if (role == "run")
    builder.Services.AddDirectoryWorkers();

using var host = builder.Build();
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
if (role == "setup")
{
    await using var setup = host.Services.CreateAsyncScope();
    await setup.ServiceProvider.GetRequiredService<SalesDbContext>().Database.MigrateAsync(token);
    await setup
        .ServiceProvider.GetRequiredService<DispatchDbContext>()
        .Database.MigrateAsync(token);
    return;
}

if (role == "run")
{
    await host.RunAsync();
    return;
}

string organization = builder.Configuration["Organization"] ?? "wholesale-alpha";
if (role == "populate")
{
    while (!await DirectoryComposition.CopyNextAsync(host.Services, organization, token)) { }

    return;
}

await using var operation = host.Services.CreateAsyncScope();
DirectoryComposition.Establish(operation.ServiceProvider, organization);
var directory = operation.ServiceProvider.GetRequiredService<ICustomerDirectory>();
if (role == "seed")
{
    var database = operation.ServiceProvider.GetRequiredService<SalesDbContext>();
    SalesDemoSeed.Stage(database);
    await database.SaveChangesAsync(token);
    return;
}

object result = role switch
{
    "search" => await directory.SearchAsync(builder.Configuration["Text"] ?? "", token),
    "progress" => await directory.ProgressAsync(token),
    "withdraw" or "relist" => await operation
        .ServiceProvider.GetRequiredService<ICustomerDeliveryDirectory>()
        .SetListedAsync(
            Guid.Parse(Required("CustomerId")),
            long.Parse(Required("Version"), System.Globalization.CultureInfo.InvariantCulture),
            role == "relist",
            token
        ),
    "edit" => await operation
        .ServiceProvider.GetRequiredService<ICustomerProfiles>()
        .ChangeAsync(
            new(
                Guid.Parse(Required("CustomerId")),
                Guid.Parse(Required("AddressId")),
                long.Parse(Required("Version"), System.Globalization.CultureInfo.InvariantCulture),
                Required("Name"),
                Required("Address")
            ),
            token
        ),
    _ => throw new InvalidOperationException(
        "Choose setup, seed, run, populate, search, progress, edit, withdraw or relist."
    ),
};
Console.WriteLine(JsonSerializer.Serialize(result));

string Required(string key) =>
    builder.Configuration[key] ?? throw new InvalidOperationException("Configure " + key + ".");
