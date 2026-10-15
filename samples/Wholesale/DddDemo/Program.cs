using ModulithFoundry.Samples.Wholesale.DddDemo;

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a fresh disposable database."
    );
await DddJourney.RunAsync(connection, Console.Out, CancellationToken.None);
