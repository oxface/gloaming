using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Worker.Maintenance;

// Trusted local operator entry. OS/database access is its admission boundary; it does not impersonate a human.
public static class BusinessOperations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        var organization = Required(configuration, "Organization");
        var operation = Required(configuration, "Operation");
        var connection = Required(configuration, "ConnectionStrings:Inventory");
        var services = new ServiceCollection();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddInventoryDatabase(connection);
        services.AddStockPositionCommands();
        services.AddStockPositionQueries();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using var scope = provider.CreateAsyncScope();
        var operationServices = scope.ServiceProvider;
        operationServices
            .GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        operationServices
            .GetRequiredService<IActorContextInitializer>()
            .Initialize(new(Actor.System(new ActorId("wholesale.operations"))));
        var database = operationServices.GetRequiredService<InventoryDbContext>();
        // Runtime never migrates/seeds/resets storage. Provision module migrations separately.
        if (operation == "read")
        {
            var id = Guid.Parse(Required(configuration, "Id"));
            var current = await operationServices
                .GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(id, cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(current, Json));
            return current is null ? 2 : 0;
        }

        using var input = JsonDocument.Parse(
            await File.ReadAllTextAsync(Required(configuration, "Input"), cancellationToken)
        );
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var commands = operationServices.GetRequiredService<IStockPositionCommands>();
        var result = operation switch
        {
            "open" => await commands.OpenAsync(Read<OpenStockPosition>(input), cancellationToken),
            "receive" => await commands.ReceiveAsync(Read<ReceiveStock>(input), cancellationToken),
            "issue" => await commands.IssueAsync(Read<IssueStock>(input), cancellationToken),
            _ => throw new ArgumentException(
                "Inventory operations are open, receive, issue and read."
            ),
        };
        bool changed = result is StockPositionChangeResult.Changed;

        if (changed)
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    committed = changed,
                    outcome = result.GetType().Name,
                    result,
                },
                Json
            )
        );
        return changed ? 0 : 2;
    }

    private static T Read<T>(JsonDocument input)
        where T : class =>
        input.RootElement.Deserialize<T>(Json)
        ?? throw new JsonException("Input must not be null.");

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException("Configure " + key + ".");
}
