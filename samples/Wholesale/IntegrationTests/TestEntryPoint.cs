using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

namespace Gloaming.Samples.Wholesale.IntegrationTests;

// Finite fixture journeys and crash cuts are executable proofs owned exclusively by this test project.
internal static class TestEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--scenario", var scenario])
        {
            var connection =
                Environment.GetEnvironmentVariable("WHOLESALE_SCENARIO_CONNECTION")
                ?? throw new InvalidOperationException(
                    "A scenario child needs its disposable database."
                );
            var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (scenario is "receipt-before-commit" or "receipt-after-commit")
                await Reactions.CommandReceiptProcess.RunAsync(
                    connection,
                    scenario == "receipt-after-commit",
                    deadline.Token
                );
            else if (scenario == "schema-evolution")
                await SchemaEvolutionJourney.RunAsync(
                    connection,
                    fixtures,
                    Console.Out,
                    deadline.Token
                );
            else if (scenario == "history")
            {
                await DemoJourneys.RunAsync(connection, fixtures, Console.Out, deadline.Token);
                await AppendJourneys.RunAsync(connection, Console.Out, deadline.Token);
                await StockIssueJourney.RunAsync(connection, Console.Out, deadline.Token);
                await RebuildJourney.RunAsync(connection, Console.Out, deadline.Token);
            }
            else
                throw new ArgumentException("Unknown test-owned scenario.");

            return 0;
        }

        // Preserve the installed xUnit/MTP native entry behavior for normal test discovery/execution.
        return args.Any(argument => argument is "--server" or "--internal-msbuild-node")
            ? await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(
                args,
                global::Wholesale
                    .IntegrationTests
                    .SelfRegisteredExtensions
                    .AddSelfRegisteredExtensions
            )
            : await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args);
    }
}
