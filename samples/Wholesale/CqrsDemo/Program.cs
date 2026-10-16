using ModulithFoundry.Samples.Wholesale.CqrsDemo;
using ModulithFoundry.Samples.Wholesale.CqrsDemo.TenantIsolation;

if (args is ["--tenant-isolation"])
{
    string migration =
        Environment.GetEnvironmentVariable("WHOLESALE_MIGRATION_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "Configure the separate migration-owner connection."
        );
    string runtime =
        Environment.GetEnvironmentVariable("WHOLESALE_RUNTIME_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Configure the restricted runtime connection.");
    await TenantIsolationJourney.RunAsync(migration, runtime, Console.Out, CancellationToken.None);
    return;
}

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a fresh disposable database."
    );
await CqrsJourney.RunAsync(connection, Console.Out, CancellationToken.None);
