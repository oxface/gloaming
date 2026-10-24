using System.Text.Json;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Gloaming.Tests.Infrastructure;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed partial class SalesDispatchTests
{
    [Fact(Timeout = 60_000)]
    public async Task RestrictedInspectionProcessReadsBothTenantsWithoutBrokerAndLeavesClaimsUnchanged()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await proof.Workflow.StartAsync(Request(), token, WorkflowProof.Beta);
        var environment = proof.Environment;
        environment.Remove("ConnectionStrings__RabbitMq");
        await using var inspection = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            environment,
            "--role",
            "inspect-sales-outbox"
        );
        Assert.True(await inspection.WaitForExitAsync(token) == 0, inspection.Output);
        var lines = inspection.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        foreach (string line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var result = document.RootElement;
            var backlog = result.GetProperty("Backlog");
            long expected =
                result.GetProperty("RouteKey").GetString() == "inventory.issue-stock" ? 2 : 0;
            Assert.Equal(expected, backlog.GetProperty("Pending").GetInt64());
            Assert.Equal(expected, backlog.GetProperty("Eligible").GetInt64());
            Assert.Equal(0, backlog.GetProperty("Leased").GetInt64());
        }

        Assert.Equal(
            2,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 0 AND revision = 0 AND lease_token IS NULL",
                token
            )
        );
        Assert.DoesNotContain("Payload", inspection.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TenantKey", inspection.Output, StringComparison.OrdinalIgnoreCase);
    }
}
