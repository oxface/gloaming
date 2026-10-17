using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Gloaming.Tenancy.EntityFrameworkCore.Postgres;

/// <summary>Explicit transaction-local PostgreSQL context for an established operation tenant.</summary>
public static class TenantDatabaseContextExtensions
{
    /// <summary>
    /// Sets a consumer-selected custom setting on the DbContext's current native transaction.
    /// The caller owns admission, policies, credentials, transaction lifetime, saves and retries.
    /// </summary>
    /// <param name="database">The context whose connection will execute protected SQL.</param>
    /// <param name="tenancy">The immutable tenant accessor from the context's owning operation.</param>
    /// <param name="settingName">A qualified custom PostgreSQL setting name, with no sample default.</param>
    /// <param name="cancellationToken">Cancels setup; failure must abort the database attempt.</param>
    public static async Task EstablishTenantDatabaseContextAsync(
        this DbContext database,
        ITenantContextAccessor tenancy,
        string settingName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(tenancy);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        if (!settingName.Contains('.'))
            throw new ArgumentException(
                "Use a qualified custom PostgreSQL setting name.",
                nameof(settingName)
            );

        string key = tenancy.Current.RequireTenant().Value;
        var transaction =
            database.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Begin the owning native transaction before tenant setup."
            );
        if (
            database.Database.GetDbConnection() is not NpgsqlConnection connection
            || connection.State != ConnectionState.Open
            || transaction.GetDbTransaction() is not NpgsqlTransaction native
            || !ReferenceEquals(native.Connection, connection)
        )
            throw new InvalidOperationException(
                "Tenant setup must use the protected PostgreSQL connection and transaction."
            );

        await using var command = new NpgsqlCommand(
            "SELECT set_config(@setting, @tenant, true)",
            connection,
            native
        );
        command.Parameters.AddWithValue("setting", settingName);
        command.Parameters.AddWithValue("tenant", key);
        if (
            await command.ExecuteScalarAsync(cancellationToken) is not string actual
            || actual != key
        )
            throw new InvalidOperationException("Database tenant context was not established.");
    }
}
