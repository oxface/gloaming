using Npgsql;
using RelationalOwnershipConsumer;

var supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Configure the disposable package-proof PostgreSQL server."
    );
await using var setup = new NpgsqlConnection(supplied);
await setup.OpenAsync();
foreach (var valueShapes in new[] { false, true })
{
    foreach (var ownershipFirst in new[] { false, true })
    {
        var name = "inline_ownership_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", setup))
            await create.ExecuteNonQueryAsync();

        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(supplied)
            {
                Database = name,
                Pooling = false,
            };
            if (valueShapes)
                await ValueShapeJourney.RunAsync(isolated.ConnectionString, ownershipFirst);
            else
                await RelationalOwnershipJourney.RunAsync(
                    isolated.ConnectionString,
                    ownershipFirst
                );
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", setup);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

Console.WriteLine(
    "PASS tenant-owned inline/nested/optional values, structs, primitive collections, JSON, both registration orders, sync/async saves, isolation, concurrency and rollback"
);
