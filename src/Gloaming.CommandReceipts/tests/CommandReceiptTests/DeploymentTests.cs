using System.Reflection;
using Gloaming.Tests.Infrastructure;
using Npgsql;
using Current = Gloaming.CommandReceipts.Deployment.Current.Application;
using Earlier = Gloaming.CommandReceipts.Deployment.Earlier.Application;

namespace Gloaming.CommandReceipts.Tests;

public sealed class DeploymentTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> EnvironmentFor(string connection) =>
        new() { ["RECEIPT_DEPLOYMENT_CONNECTION"] = connection };

    [Fact]
    public async Task NewConsumerReadsOldResultAndPreservesV1ComparisonAcrossRequestEvolution()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        Assert.Contains(
            "EXECUTE",
            await RunAsync(connection, typeof(Earlier).Assembly, id, "original"),
            StringComparison.Ordinal
        );
        string replay = await RunAsync(connection, typeof(Current).Assembly, id, "original");
        Assert.Contains("REPLAY", replay, StringComparison.Ordinal);
        Assert.Contains("original", replay, StringComparison.Ordinal);
        Assert.DoesNotContain("EXECUTE", replay, StringComparison.Ordinal);
        await RunAsync(connection, typeof(Current).Assembly, id, "changed", expectedExit: 11);
        Assert.Equal(1, await ReceiptTests.CountAsync(connection, "deployment.effects", Token));
        Assert.Equal(
            1,
            await ReceiptTests.CountAsync(connection, "deployment.logical_actions", Token)
        );
    }

    [Fact]
    public async Task OldReaderRejectsNewResultWithoutExecutingAndCompatibleConsumerCanRecoverIt()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await RunAsync(connection, typeof(Current).Assembly, id, "new-result");
        string refused = await RunAsync(
            connection,
            typeof(Earlier).Assembly,
            id,
            "new-result",
            expectedExit: 10
        );
        Assert.Contains("INCOMPATIBLE_READER", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("EXECUTE", refused, StringComparison.Ordinal);
        Assert.Contains(
            "REPLAY",
            await RunAsync(connection, typeof(Current).Assembly, id, "new-result"),
            StringComparison.Ordinal
        );
        Assert.Equal(1, await ReceiptTests.CountAsync(connection, "deployment.effects", Token));
    }

    [Fact]
    public async Task EarlierAndCurrentProcessesCoordinateTheSameKeyThroughCommit()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        string release = Path.Combine(
            Path.GetTempPath(),
            "receipt-release-" + Guid.NewGuid().ToString("N")
        );
        var environment = EnvironmentFor(connection);
        environment["RECEIPT_DEPLOYMENT_PAUSE"] = "before";
        environment["RECEIPT_DEPLOYMENT_RELEASE_FILE"] = release;
        try
        {
            await using var first = new ChildHost(
                typeof(Earlier).Assembly,
                environment,
                id.ToString("D"),
                "same"
            );
            await first.WaitForOutputAsync("BEFORE_COMMIT", Token);
            await using var second = new ChildHost(
                typeof(Current).Assembly,
                EnvironmentFor(connection),
                id.ToString("D"),
                "same"
            );
            await ReceiptTests.WaitForWaiterAsync(connection, Token);
            await File.WriteAllTextAsync(release, "commit", Token);
            Assert.Equal(0, await first.WaitForExitAsync(Token));
            Assert.Equal(0, await second.WaitForExitAsync(Token));
            Assert.Contains("REPLAY", second.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("EXECUTE", second.Output, StringComparison.Ordinal);
            Assert.Equal(1, await ReceiptTests.CountAsync(connection, "deployment.effects", Token));
        }
        finally
        {
            File.Delete(release);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KilledEarlierConsumerRecoversCommittedOrRolledBackWorkInCurrentProcess(
        bool committed
    )
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        var environment = EnvironmentFor(connection);
        environment["RECEIPT_DEPLOYMENT_PAUSE"] = committed ? "after" : "before";
        environment["RECEIPT_DEPLOYMENT_RELEASE_FILE"] = Path.Combine(
            Path.GetTempPath(),
            "unused-receipt-release-" + Guid.NewGuid().ToString("N")
        );
        await using (
            var old = new ChildHost(
                typeof(Earlier).Assembly,
                environment,
                id.ToString("D"),
                "retained"
            )
        )
        {
            await old.WaitForOutputAsync(committed ? "COMMITTED" : "BEFORE_COMMIT", Token);
            Assert.Equal(
                committed ? 1 : 0,
                await ReceiptTests.CountAsync(connection, "deployment.logical_actions", Token)
            );
            await old.KillAsync(Token);
        }

        string recovered = await RunAsync(connection, typeof(Current).Assembly, id, "retained");
        Assert.Contains(committed ? "REPLAY" : "EXECUTE", recovered, StringComparison.Ordinal);
        Assert.Equal(1, await ReceiptTests.CountAsync(connection, "deployment.effects", Token));
        Assert.Equal(
            1,
            await ReceiptTests.CountAsync(connection, "deployment.logical_actions", Token)
        );
    }

    [Fact]
    public async Task FrozenV1LockVectorStillCoordinatesThePublicStore()
    {
        string connection = await SetupAsync();
        await using var holder = new NpgsqlConnection(connection);
        await holder.OpenAsync(Token);
        await using var transaction = await holder.BeginTransactionAsync(Token);
        // Published v1 framing vector, calculated independently of the library implementation.
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(-2187035804754743166::bigint)",
            holder,
            transaction
        );
        await command.ExecuteNonQueryAsync(Token);
        await using var child = new ChildHost(
            typeof(Current).Assembly,
            EnvironmentFor(connection),
            "1d700000-0000-0000-0000-000000000001",
            "vector"
        );
        await ReceiptTests.WaitForWaiterAsync(connection, Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(0, await child.WaitForExitAsync(Token));
        Assert.Contains("EXECUTE", child.Output, StringComparison.Ordinal);
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var setup = new ChildHost(
            typeof(Earlier).Assembly,
            EnvironmentFor(connection),
            "setup"
        );
        Assert.Equal(0, await setup.WaitForExitAsync(Token));
        return connection;
    }

    private static async Task<string> RunAsync(
        string connection,
        Assembly binary,
        Guid id,
        string input,
        int expectedExit = 0
    )
    {
        await using var child = new ChildHost(
            binary,
            EnvironmentFor(connection),
            id.ToString("D"),
            input
        );
        Assert.Equal(expectedExit, await child.WaitForExitAsync(Token));
        return child.Output;
    }
}
