using JsonOwnershipConsumer;
using Npgsql;

var supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Configure the disposable package-proof PostgreSQL server."
    );
var name = "json_ownership_" + Guid.NewGuid().ToString("N");
await using var setup = new NpgsqlConnection(supplied);
await setup.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", setup))
    await create.ExecuteNonQueryAsync();

try
{
    var isolated = new NpgsqlConnectionStringBuilder(supplied) { Database = name, Pooling = false };
    await JsonOwnershipJourney.RunAsync(isolated.ConnectionString);
    Console.WriteLine(
        "PASS tenant-owned native EF JSON records/collections, sync/async saves, isolation, concurrency and rollback"
    );
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", setup);
    await drop.ExecuteNonQueryAsync();
}
