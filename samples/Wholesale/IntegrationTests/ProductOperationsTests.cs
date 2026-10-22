using System.Text.Json;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Worker.Maintenance;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests;

public sealed class ProductOperationsTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("inventory")]
    [InlineData("purchasing")]
    public async Task CanonicalProductSetupAndWorkerOperationsPersistModuleStateAndRejectStaleCommands(
        string role
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var connection = await postgres.CreateDatabaseAsync(token);
        await using (
            var setup = new ChildHost(
                typeof(DemoComposition).Assembly,
                new() { ["ConnectionStrings__Access"] = connection },
                "--initialize-demo"
            )
        )
            Assert.True(await setup.WaitForExitAsync(token) == 0, setup.Output);

        var id = Guid.NewGuid();
        object first =
            role == "inventory"
                ? new
                {
                    id,
                    stockItemId = Guid.NewGuid(),
                    stockingLocationId = Guid.NewGuid(),
                    baseUnitCode = "EA",
                }
                : new
                {
                    id,
                    code = "PRODUCT-PURCHASE",
                    supplierReference = "SUPPLIER",
                    currency = "EUR",
                };
        object second =
            role == "inventory"
                ? new
                {
                    id,
                    expectedVersion = 1,
                    receipts = new[] { new { quantity = 5, deliveryReference = "DELIVERY" } },
                }
                : new
                {
                    id,
                    expectedVersion = 1,
                    lines = new[]
                    {
                        new
                        {
                            itemCode = "DEMO-NOTEBOOK",
                            quantity = 2,
                            unitPrice = 10,
                        },
                    },
                };
        await Command(role == "inventory" ? "open" : "draft", first, 0);
        await Command(role == "inventory" ? "receive" : "change-lines", second, 0);
        await Command(role == "inventory" ? "receive" : "change-lines", second, 2);
        var environment = EnvironmentFor();
        environment["Operation"] = "read";
        environment["Id"] = id.ToString("D");
        await using (
            var read = new ChildHost(
                typeof(BusinessOperations).Assembly,
                environment,
                "--role",
                role
            )
        )
        {
            Assert.True(await read.WaitForExitAsync(token) == 0, read.Output);
            var current = JsonSerializer.Deserialize<JsonElement>(read.Output);
            Assert.Equal(2, current.GetProperty("version").GetInt64());
        }

        environment["Organization"] = "wholesale-beta";
        await using var foreign = new ChildHost(
            typeof(BusinessOperations).Assembly,
            environment,
            "--role",
            role
        );
        Assert.Equal(2, await foreign.WaitForExitAsync(token));
        Assert.Equal("null", foreign.Output.Trim());

        Dictionary<string, string> EnvironmentFor() =>
            new()
            {
                ["ConnectionStrings__" + (role == "inventory" ? "Inventory" : "Purchasing")] =
                    connection,
                ["Organization"] = "wholesale-alpha",
            };

        async Task Command(string operation, object input, int exit)
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(input), token);
                var settings = EnvironmentFor();
                settings["Operation"] = operation;
                settings["Input"] = path;
                await using var child = new ChildHost(
                    typeof(BusinessOperations).Assembly,
                    settings,
                    "--role",
                    role
                );
                Assert.True(await child.WaitForExitAsync(token) == exit, child.Output);
                Assert.Equal(
                    exit == 0,
                    JsonSerializer
                        .Deserialize<JsonElement>(child.Output)
                        .GetProperty("committed")
                        .GetBoolean()
                );
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
