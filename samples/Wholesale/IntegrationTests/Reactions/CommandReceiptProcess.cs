using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.DomainEvents;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reactions;

internal static class CommandReceiptProcess
{
    internal static async Task RunAsync(string connection, bool commit, CancellationToken token)
    {
        await using var provider = DomainEventComposition
            .CreateServices(connection)
            .BuildServiceProvider();
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var result = await scope
            .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
            .SubmitAsync(
                new(
                    DomainEventJourney.FirstOrderId,
                    1,
                    Guid.Parse(
                        Environment.GetEnvironmentVariable("WHOLESALE_RECEIPT_OPERATION_ID")!
                    )
                ),
                token
            );
        if (result is not DraftOrderChangeResult.Changed)
            throw new InvalidOperationException(
                "The crash proof must reach an accepted submission."
            );

        await database.SaveChangesAsync(token);
        if (commit)
            await transaction.CommitAsync(token);

        Console.WriteLine("receipt-cut-ready");
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
}
