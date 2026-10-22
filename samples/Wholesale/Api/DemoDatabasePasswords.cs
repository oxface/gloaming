using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.Api;

// Aspire supplies endpoint/username references and separate secret password parameters.
// Normalize once before service registration/setup; Npgsql owns connection-string escaping.
internal static class DemoDatabasePasswords
{
    internal static void Apply(IConfiguration configuration)
    {
        foreach (string name in new[] { "Access", "Sales" })
        {
            if (configuration[$"RuntimePasswords:{name}"] is not { } password)
                continue;

            string connection =
                configuration.GetConnectionString(name)
                ?? throw new InvalidOperationException(
                    $"Configure ConnectionStrings:{name} with its runtime password."
                );
            configuration[$"ConnectionStrings:{name}"] = new NpgsqlConnectionStringBuilder(
                connection
            )
            {
                Password = password,
            }.ConnectionString;
        }
    }
}
