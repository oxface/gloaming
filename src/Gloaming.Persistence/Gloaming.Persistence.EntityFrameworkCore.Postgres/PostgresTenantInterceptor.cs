using System.Data;
using System.Data.Common;
using System.Transactions;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Persistence.EntityFrameworkCore.Postgres;

// One scoped native interceptor: initialize each EF-started transaction, then require
// that transaction for protected commands. There is no connection-lease readiness cache.
internal sealed class PostgresTenantInterceptor<TContext>(IServiceProvider provider)
    : DbTransactionInterceptor,
        IDbCommandInterceptor
    where TContext : DbContext
{
    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result
    )
    {
        try
        {
            string? setting = Setting(eventData.Context);
            if (setting is null)
                return result;

            using var command = Setup(connection, result, setting);
            Verify(command.ExecuteScalar());
            return result;
        }
        catch
        {
            // EF has not yet published CurrentTransaction, so the caller cannot dispose it.
            // Disposing a newly started native transaction rolls back failed/cancelled setup.
            result.Dispose();
            throw;
        }
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string? setting = Setting(eventData.Context);
            if (setting is null)
                return result;

            await using var command = Setup(connection, result, setting);
            Verify(await command.ExecuteScalarAsync(cancellationToken));
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }

    public override DbTransaction TransactionUsed(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result
    )
    {
        if (Setting(eventData.Context) is not null)
            throw new InvalidOperationException(
                "Begin the RLS transaction through this DbContext; adopted transactions are unsupported."
            );

        return result;
    }

    public override ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(TransactionUsed(connection, eventData, result));

    internal void ValidateSave(TContext context)
    {
        if (Setting(context) is null)
            return;

        if (
            context
                .GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()
                ?.Interceptors?.Any(interceptor => ReferenceEquals(interceptor, this)) != true
        )
            throw new InvalidOperationException(
                "Install UseRls for this DbContext's scoped options."
            );

        Check(context);
    }

    private static string? Setting(DbContext? context) =>
        context is TContext database
            ? TenantRowSecurityExtensions.Setting(database.Model)
            : throw new InvalidOperationException("Use context-specific PostgreSQL RLS options.");

    private string Tenant =>
        provider.GetRequiredService<ITenantContextAccessor>().Current.RequireTenant().Value;

    private void Check(
        DbContext? context,
        DbConnection? connection = null,
        DbTransaction? transaction = null
    )
    {
        if (Setting(context) is null)
            return;

        _ = Tenant;
        var current = context!.Database.CurrentTransaction?.GetDbTransaction();
        RequireTransaction(context.Database.GetDbConnection(), current);
        if (
            connection is not null
            && (
                !ReferenceEquals(connection, current!.Connection)
                || !ReferenceEquals(transaction, current)
            )
        )
            throw new InvalidOperationException(
                "Protected SQL must use the context's owning RLS transaction."
            );
    }

    private NpgsqlCommand Setup(DbConnection connection, DbTransaction transaction, string setting)
    {
        RequireTransaction(connection, transaction);
        var options = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (options.NoResetOnClose || options.Multiplexing)
            throw new InvalidOperationException(
                "Use reset-enabled, non-multiplexed Npgsql connections."
            );

        var command = new NpgsqlCommand(
            """
            SELECT CASE WHEN NULLIF(current_setting(@setting, true), '') IS NULL
                THEN set_config(@setting, @tenant, true) END
            """,
            (NpgsqlConnection)connection,
            (NpgsqlTransaction)transaction
        );
        command.Parameters.AddWithValue("setting", setting);
        command.Parameters.AddWithValue("tenant", Tenant);
        return command;
    }

    private void Verify(object? result)
    {
        if (result is not string actual || actual != Tenant)
            throw new InvalidOperationException(
                "RLS requires an empty tenant baseline and successful transaction-local tenant setup."
            );
    }

    private static void RequireTransaction(DbConnection connection, DbTransaction? transaction)
    {
        if (
            Transaction.Current is not null
            || connection is not NpgsqlConnection
            || connection.State != ConnectionState.Open
            || transaction is not NpgsqlTransaction
            || !ReferenceEquals(transaction.Connection, connection)
        )
            throw new InvalidOperationException(
                "Begin the owning native PostgreSQL transaction before protected reads or writes; ambient transactions are unsupported."
            );
    }

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Check(eventData.Context, command.Connection, command.Transaction);
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Check(eventData.Context, command.Connection, command.Transaction);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Check(eventData.Context, command.Connection, command.Transaction);
        return result;
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Check(eventData.Context, command.Connection, command.Transaction);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        Check(eventData.Context, command.Connection, command.Transaction);
        return result;
    }

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Check(eventData.Context, command.Connection, command.Transaction);
        return ValueTask.FromResult(result);
    }
}
