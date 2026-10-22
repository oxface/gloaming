using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Directory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Worker.Directory;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task CanonicalWorkerPopulatesThenConsumesActualHttpEditsWithRestrictedRelayAndReceiverOnlySearch()
    {
        await using var app = await StartAsync();
        string source = app.Configuration.GetConnectionString("Access")!;
        string receiver = await postgres.CreateDatabaseAsync(Token);
        var both = new Dictionary<string, string>
        {
            ["ConnectionStrings__Sales"] = source,
            ["ConnectionStrings__Dispatch"] = receiver,
            ["Organization"] = DirectoryProof.Alpha,
        };
        await DirectoryCommandAsync("setup", both);
        await DirectoryCommandAsync(
            "admit",
            new()
            {
                ["ConnectionStrings__Dispatch"] = receiver,
                ["Organization"] = DirectoryProof.Alpha,
            }
        );
        string populated = await DirectoryCommandAsync("populate", both);
        Assert.Contains("\"ready\":true", populated, StringComparison.Ordinal);
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        using var edit = EditRequest(session);
        using var response = await client.SendAsync(edit, Token);
        response.EnsureSuccessStatusCode();

        string restricted = new NpgsqlConnectionStringBuilder(source)
        {
            Username = "directory_" + Guid.NewGuid().ToString("N"),
            Password = "directory-proof-password",
        }.ConnectionString;
        await using (
            var setup = new ChildHost(
                typeof(DirectoryHost).Assembly,
                new()
                {
                    ["ConnectionStrings__SalesDispatchSetup"] = source,
                    ["ConnectionStrings__SalesDispatch"] = restricted,
                },
                "--role",
                "setup-sales-dispatch"
            )
        )
            Assert.True(await setup.WaitForExitAsync(Token) == 0, setup.Output);

        // Only the matching Directory route is eligible; a business publisher cannot consume it.
        string relayed = await DirectoryCommandAsync(
            "relay",
            new()
            {
                ["ConnectionStrings__SalesDispatch"] = restricted,
                ["ConnectionStrings__Dispatch"] = receiver,
            }
        );
        Assert.Contains(
            "DIRECTORY " + (int)OutboxDispatchResult.Published,
            relayed,
            StringComparison.Ordinal
        );
        await DirectoryCommandAsync(
            "process",
            new() { ["ConnectionStrings__Dispatch"] = receiver }
        );
        await ExecuteAsync(app, "ALTER TABLE sales.customer_profiles RENAME TO source_unavailable");
        string searched = await DirectoryCommandAsync(
            "search",
            new()
            {
                ["ConnectionStrings__Dispatch"] = receiver,
                ["Organization"] = DirectoryProof.Alpha,
                ["Text"] = "Changed Alpha",
            }
        );
        string result = searched
            .Split('\n')
            .Single(line => line.StartsWith("DIRECTORY ", StringComparison.Ordinal))[10..];
        using var json = JsonDocument.Parse(result);
        var customer = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(SalesDemoSeed.AlphaCustomerId, customer.GetProperty("customerId").GetGuid());
        Assert.Equal("Changed Street", customer.GetProperty("addressLine").GetString());
        await using (
            var rejected = new ChildHost(
                typeof(DirectoryHost).Assembly,
                new()
                {
                    ["ConnectionStrings__Dispatch"] = receiver,
                    ["Organization"] = "forged-organization",
                },
                "--role",
                "directory",
                "--Operation",
                "search"
            )
        )
        {
            Assert.NotEqual(0, await rejected.WaitForExitAsync(Token));
            Assert.Contains("Dispatch has not admitted", rejected.Output, StringComparison.Ordinal);
        }

        await ExecuteAsync(app, "ALTER TABLE sales.source_unavailable RENAME TO customer_profiles");
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                source,
                "SELECT count(*) FROM sales.outbox_messages WHERE destination = 'dispatch.customer-directory' AND dispatched_at IS NOT NULL AND attempts = 1",
                Token
            )
        );
    }

    private static async Task<string> DirectoryCommandAsync(
        string operation,
        Dictionary<string, string> environment
    )
    {
        await using var process = new ChildHost(
            typeof(DirectoryHost).Assembly,
            environment,
            "--role",
            "directory",
            "--Operation",
            operation
        );
        Assert.True(await process.WaitForExitAsync(Token) == 0, process.Output);
        return process.Output;
    }
}
