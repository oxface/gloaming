using Gloaming.Samples.Wholesale.Api;
using Gloaming.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Theory]
    [InlineData("ConnectionStrings__Setup")]
    [InlineData("ConnectionStrings__SalesMigrations")]
    [InlineData("Parameters__postgres-password")]
    public async Task RlsHttpRejectsProvisioningCredentialsBeforeOpeningRuntimeConnections(
        string key
    )
    {
        var environment = new Dictionary<string, string>
        {
            ["ConnectionStrings__Access"] =
                "Host=127.0.0.1;Port=1;Database=unused;Username=runtime",
            ["ConnectionStrings__Sales"] = "Host=127.0.0.1;Port=1;Database=unused;Username=runtime",
            ["ConnectionStrings__Setup"] = "",
            ["ConnectionStrings__SalesMigrations"] = "",
            ["Parameters__postgres-password"] = "",
        };
        environment[key] = "deliberately-forbidden-provisioning-value";
        await using var process = new ChildHost(typeof(DemoComposition).Assembly, environment);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        Assert.NotEqual(0, await process.WaitForExitAsync(timeout.Token));
        Assert.Contains(
            "Keep provisioning credentials out",
            process.StandardError,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "deliberately-forbidden-provisioning-value",
            process.StandardError,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Now listening", process.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("insecure-issuer")]
    [InlineData("duplicate-subjects")]
    public async Task InvalidExplicitDemoIdentityInputsFailBeforeCreatingSchema(string fault)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var process = new ChildHost(
            typeof(DemoComposition).Assembly,
            new()
            {
                ["ConnectionStrings__Access"] = connection,
                ["DemoIdentity__Issuer"] =
                    fault == "insecure-issuer" ? "http://identity.test" : "https://identity.test",
                ["DemoIdentity__AlphaSubject"] = "alpha-subject",
                ["DemoIdentity__BetaSubject"] =
                    fault == "partial" ? ""
                    : fault == "duplicate-subjects" ? "alpha-subject"
                    : "beta-subject",
            },
            "--initialize-demo"
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        Assert.NotEqual(0, await process.WaitForExitAsync(timeout.Token));
        Assert.Contains("Configure a HTTPS DemoIdentity:Issuer", process.StandardError);
        Assert.DoesNotContain("demo initialized", process.StandardOutput);

        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('access.users') IS NULL AND to_regclass('inventory.stock_availability') IS NULL AND to_regclass('sales.customer_profiles') IS NULL",
            database
        );
        Assert.Equal(true, await command.ExecuteScalarAsync(Token));
    }
}
