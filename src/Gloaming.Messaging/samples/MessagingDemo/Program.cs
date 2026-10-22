using ModulithFoundry.Samples.MessagingDemo;

if (args.Length > 1 || args.Length == 1 && args[0] != "--schema-compatibility")
    throw new ArgumentException("Use --schema-compatibility or the default journey.");

string sender =
    Environment.GetEnvironmentVariable("MESSAGING_DEMO_SENDER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set MESSAGING_DEMO_SENDER_CONNECTION_STRING.");
string receiver =
    Environment.GetEnvironmentVariable("MESSAGING_DEMO_RECEIVER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set MESSAGING_DEMO_RECEIVER_CONNECTION_STRING.");
string broker =
    Environment.GetEnvironmentVariable("MESSAGING_DEMO_RABBITMQ")
    ?? throw new InvalidOperationException("Set MESSAGING_DEMO_RABBITMQ.");
if (args.Length == 1)
    await SchemaCompatibilityJourney.RunAsync(
        sender,
        receiver,
        new Uri(broker),
        Console.Out,
        CancellationToken.None
    );
else
    await MessagingJourney.RunAsync(
        sender,
        receiver,
        new Uri(broker),
        Console.Out,
        CancellationToken.None
    );
