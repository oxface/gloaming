using ModulithFoundry.Samples.EventStorageDemo;

string connection =
    Environment.GetEnvironmentVariable("EVENT_STORAGE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Supply a disposable EVENT_STORAGE_DEMO_CONNECTION_STRING."
    );
await DemoJourneys.RunAsync(connection, Console.Out, CancellationToken.None);
await CounterAppendJourney.RunAsync(connection, Console.Out, CancellationToken.None);
