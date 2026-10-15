using ModulithFoundry.Samples.EventReactions;

string connection =
    Environment.GetEnvironmentVariable("DOMAIN_EVENTS_ASSESSMENT_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set DOMAIN_EVENTS_ASSESSMENT_CONNECTION_STRING to a fresh disposable database."
    );
await ExtractionJourney.RunAsync(connection, Console.Out, CancellationToken.None);
