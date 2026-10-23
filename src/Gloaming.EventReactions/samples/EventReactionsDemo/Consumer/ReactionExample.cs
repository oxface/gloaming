using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.EventReactions;

public static class ReactionExample
{
    public static Guid Id { get; } = Guid.Parse("d21a0000-0000-0000-0000-000000000002");

    public static async Task RunAsync(string connection, TextWriter output, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output);

        var services = new ServiceCollection();
        services.AddMappedQuotesModule(connection);
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setup = setupScope.ServiceProvider.GetRequiredService<MappedQuotesContext>();
            await setup.Database.MigrateAsync(token);
            setup.Allowances.Add(
                new()
                {
                    Owner = "alpha",
                    Ceiling = 100,
                    Version = 1,
                }
            );
            setup.Quotes.Add(
                new()
                {
                    Owner = "alpha",
                    Id = Id,
                    Amount = 60,
                    Version = 1,
                }
            );
            await setup.SaveChangesAsync(token);
        }

        await using (var operation = provider.CreateAsyncScope())
        {
            var database = operation.ServiceProvider.GetRequiredService<MappedQuotesContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            await operation
                .ServiceProvider.GetRequiredService<MappedQuoteCommands>()
                .AcceptAsync(Id, token);
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        await using var observer = provider.CreateAsyncScope();
        var read = observer.ServiceProvider.GetRequiredService<MappedQuotesContext>();
        if (
            (await read.Allowances.SingleAsync(token)).Reserved != 60
            || await read.Receipts.CountAsync(token) != 1
            || await read.Allocations.CountAsync(token) != 1
        )
            throw new InvalidOperationException(
                "The committed reaction effects differ from the expected outcome."
            );

        await output.WriteLineAsync(
            "Immutable quote: explicit sink and bounded cascade; reserved=60, receipt=1, allocation=1."
        );
    }
}
