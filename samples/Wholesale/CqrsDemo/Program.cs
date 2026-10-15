using ModulithFoundry.Samples.Wholesale.CqrsDemo;

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a fresh disposable database."
    );
await CqrsJourney.RunAsync(connection, Console.Out, CancellationToken.None);
