namespace AgentPersistenceDemo;

internal static class Program
{
    public static async Task Main()
    {
        var connection =
            Environment.GetEnvironmentVariable("GLOAMING_AGENT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Configure a disposable PostgreSQL database.");
        await AdoptionJourney.RunJourneyAsync(connection);
        Console.WriteLine(
            "PASS reconstructed native start/refine/accept; acceptance made no model call."
        );
    }
}
