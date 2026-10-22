using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Queries;

internal sealed class QueryProbe(Func<Task>? afterCount = null) : DbCommandInterceptor
{
    internal List<string> Commands { get; } = [];
    private bool _countObserved;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        Commands.Add(command.CommandText);
        if (!_countObserved && command.CommandText.Contains("count(*)", StringComparison.Ordinal))
        {
            _countObserved = true;
            if (afterCount is not null)
                await afterCount();
        }

        return result;
    }
}
