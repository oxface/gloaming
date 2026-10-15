using ModulithFoundry.Samples.Wholesale.RichAggregateDemo;

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a fresh disposable database."
    );
await DraftOrderJourney.RunAsync(connection, Console.Out, CancellationToken.None);
