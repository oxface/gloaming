using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

if (args.FirstOrDefault() == "--regenerate-stock")
{
    if (
        args.Length != 3
        || string.IsNullOrWhiteSpace(args[1])
        || !Guid.TryParse(args[2], out var id)
        || id == Guid.Empty
    )
    {
        Console.Error.WriteLine("Usage: --regenerate-stock <organization-key> <stock-position-id>");
        Environment.ExitCode = 64;
        return;
    }

    string? maintenanceConnection = Environment.GetEnvironmentVariable(
        "WHOLESALE_DEMO_CONNECTION_STRING"
    );
    if (string.IsNullOrWhiteSpace(maintenanceConnection))
    {
        Console.Error.WriteLine(
            "Set WHOLESALE_DEMO_CONNECTION_STRING to the existing Inventory database."
        );
        Environment.ExitCode = 64;
        return;
    }

    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, signal) =>
    {
        signal.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancel;
    try
    {
        await using var maintenance = StockRegeneration
            .CreateServices(maintenanceConnection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        Environment.ExitCode = await StockRegeneration.RunAsync(
            maintenance.GetRequiredService<IServiceScopeFactory>(),
            args[1],
            id,
            Console.Out,
            cancellation.Token
        );
    }
    finally
    {
        Console.CancelKeyPress -= cancel;
    }

    return;
}

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a disposable demo database."
    );
if (args.Contains("--inbox", StringComparer.Ordinal))
{
    string broker =
        Environment.GetEnvironmentVariable("WHOLESALE_DEMO_RABBITMQ")
        ?? throw new InvalidOperationException(
            "Set WHOLESALE_DEMO_RABBITMQ to a disposable RabbitMQ broker URI."
        );
    await InboxJourney.RunAsync(connection, new Uri(broker), Console.Out, CancellationToken.None);
    return;
}
if (args.Contains("--outbox", StringComparer.Ordinal))
{
    string broker =
        Environment.GetEnvironmentVariable("WHOLESALE_DEMO_RABBITMQ")
        ?? throw new InvalidOperationException(
            "Set WHOLESALE_DEMO_RABBITMQ to a disposable RabbitMQ broker URI."
        );
    await OutboxJourney.RunAsync(connection, new Uri(broker), Console.Out, CancellationToken.None);
    return;
}
if (args.Contains("--schema-evolution", StringComparer.Ordinal))
{
    await SchemaEvolutionJourney.RunAsync(
        connection,
        Path.Combine(AppContext.BaseDirectory, "Fixtures"),
        Console.Out,
        CancellationToken.None
    );
    return;
}
await DemoJourneys.RunAsync(
    connection,
    Path.Combine(AppContext.BaseDirectory, "Fixtures"),
    Console.Out,
    CancellationToken.None
);
await AppendJourneys.RunAsync(connection, Console.Out, CancellationToken.None);
await StockIssueJourney.RunAsync(connection, Console.Out, CancellationToken.None);
await RebuildJourney.RunAsync(connection, Console.Out, CancellationToken.None);
