using AgentPersistenceDemo;
using Npgsql;

var connection =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Configure the package-proof PostgreSQL server.");
string name = "agent_package_" + Guid.NewGuid().ToString("N");
await using var setup = new NpgsqlConnection(connection);
await setup.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", setup))
    await create.ExecuteNonQueryAsync();

try
{
    var isolated = new NpgsqlConnectionStringBuilder(connection)
    {
        Database = name,
        Pooling = false,
    };
    await AdoptionJourney.RunJourneyAsync(isolated.ConnectionString);
    Console.WriteLine(
        "PASS native MAF package-only reconstruction/refine/accept; zero calls on acceptance"
    );
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", setup);
    await drop.ExecuteNonQueryAsync();
}
