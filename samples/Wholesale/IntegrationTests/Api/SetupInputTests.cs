using System.Diagnostics;
using Gloaming.Samples.Wholesale.Api;
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
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(DemoComposition).Assembly.Location);
        start.Environment["ConnectionStrings__Access"] =
            "Host=127.0.0.1;Port=1;Database=unused;Username=runtime";
        start.Environment["ConnectionStrings__Sales"] =
            "Host=127.0.0.1;Port=1;Database=unused;Username=runtime";
        foreach (
            string provisionKey in new[]
            {
                "ConnectionStrings__Setup",
                "ConnectionStrings__SalesMigrations",
                "Parameters__postgres-password",
            }
        )
            start.Environment[provisionKey] = "";

        start.Environment[key] = "deliberately-forbidden-provisioning-value";
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the guarded HTTP host.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(Token);
        try
        {
            await process.WaitForExitAsync(Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(
                "Keep provisioning credentials out",
                await errors,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "deliberately-forbidden-provisioning-value",
                await errors,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("Now listening", await output, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("insecure-issuer")]
    [InlineData("duplicate-subjects")]
    public async Task InvalidExplicitDemoIdentityInputsFailBeforeCreatingSchema(string fault)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(DemoComposition).Assembly.Location);
        start.ArgumentList.Add("--initialize-demo");
        start.Environment["ConnectionStrings__Access"] = connection;
        start.Environment["DemoIdentity__Issuer"] =
            fault == "insecure-issuer" ? "http://identity.test" : "https://identity.test";
        start.Environment["DemoIdentity__AlphaSubject"] = "alpha-subject";
        if (fault == "partial")
            start.Environment.Remove("DemoIdentity__BetaSubject");
        else
            start.Environment["DemoIdentity__BetaSubject"] =
                fault == "duplicate-subjects" ? "alpha-subject" : "beta-subject";
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Could not start demo setup.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(Token);
        try
        {
            await process.WaitForExitAsync(Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Configure a HTTPS DemoIdentity:Issuer", await errors);
            Assert.DoesNotContain("demo initialized", await output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('access.users') IS NULL AND to_regclass('inventory.stock_availability') IS NULL AND to_regclass('sales.customer_profiles') IS NULL",
            database
        );
        Assert.Equal(true, await command.ExecuteScalarAsync(Token));
    }
}
