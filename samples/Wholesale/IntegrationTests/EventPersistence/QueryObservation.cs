using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

// Observe native EF commands; no production reader hook or SQL snapshot is needed.
internal sealed class QueryObservation(string schema) : DbCommandInterceptor
{
    internal List<(string Sql, object?[] Values)> Commands { get; } = [];
    internal Func<CancellationToken, Task>? BeforeEventRead { get; set; }
    internal Func<CancellationToken, Task>? BeforeInlineRead { get; set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Commands.Add(
            (
                command.CommandText,
                command
                    .Parameters.Cast<DbParameter>()
                    .Select(parameter => parameter.Value)
                    .ToArray()
            )
        );
        if (
            command.CommandText.Contains($"FROM {schema}.events AS", StringComparison.Ordinal)
            && BeforeEventRead is { } action
        )
        {
            BeforeEventRead = null;
            await action(cancellationToken);
        }
        if (
            (
                command.CommandText.Contains(
                    $"FROM {schema}.stock_position_current AS",
                    StringComparison.Ordinal
                )
                || command.CommandText.Contains(
                    $"FROM {schema}.purchase_order_current AS",
                    StringComparison.Ordinal
                )
            ) && BeforeInlineRead is { } inlineAction
        )
        {
            BeforeInlineRead = null;
            await inlineAction(cancellationToken);
        }
        return result;
    }
}
