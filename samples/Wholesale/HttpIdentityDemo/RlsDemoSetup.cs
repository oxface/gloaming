using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo;

// Explicit, one-shot disposable-demo provisioning. Ordinary API startup never calls this.
internal static class RlsDemoSetup
{
    internal static async Task InitializeAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        var identities = DemoIdentitySetup.Read(configuration);
        string setup =
            configuration.GetConnectionString("Setup")
            ?? throw new InvalidOperationException(
                "Configure ConnectionStrings:Setup only for finite provisioning."
            );
        string access =
            configuration.GetConnectionString("Access")
            ?? throw new InvalidOperationException(
                "Configure the restricted Access runtime connection."
            );
        string sales =
            configuration.GetConnectionString("Sales")
            ?? throw new InvalidOperationException(
                "Configure the restricted Sales runtime connection."
            );
        var accessSettings = new NpgsqlConnectionStringBuilder(access);
        var salesSettings = new NpgsqlConnectionStringBuilder(sales);
        if (
            accessSettings.Username != "wholesale_access"
            || salesSettings.Username != "wholesale_sales"
            || string.IsNullOrEmpty(accessSettings.Password)
            || string.IsNullOrEmpty(salesSettings.Password)
        )
            throw new InvalidOperationException(
                "Use the two named runtime logins with nonempty passwords for this demo."
            );

        await using (var connection = new NpgsqlConnection(setup))
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var settings in new[] { accessSettings, salesSettings })
            {
                // PostgreSQL quotes both the identifier and literal. Do not alter an existing role.
                await using var format = new NpgsqlCommand(
                    "SELECT format('CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT', @role, @password)",
                    connection,
                    transaction
                );
                format.Parameters.AddWithValue("role", settings.Username!);
                format.Parameters.AddWithValue("password", settings.Password!);
                string sql = (string)(await format.ExecuteScalarAsync(cancellationToken))!;
                await using var create = new NpgsqlCommand(sql, connection, transaction);
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await DemoSetup.InitializeAsync(setup, true, cancellationToken, identities);
        await using (var connection = new NpgsqlConnection(setup))
        {
            await connection.OpenAsync(cancellationToken);
            await using var grant = new NpgsqlCommand(
                """
                GRANT USAGE ON SCHEMA access, inventory TO wholesale_access;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA access TO wholesale_access;
                GRANT SELECT ON inventory.stock_availability TO wholesale_access;
                """,
                connection
            );
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }

        await SalesHttpRowSecurity.InstallAsync(setup, sales, cancellationToken);
        await SalesHttpRowSecurity.VerifyRuntimeAsync(sales, access, cancellationToken);
    }
}
