using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.Samples.EventReactions;

public static class ExtractionJourney
{
    public static Guid DirectId { get; } = Guid.Parse("d21a0000-0000-0000-0000-000000000001");
    public static Guid MappedId { get; } = Guid.Parse("d21a0000-0000-0000-0000-000000000002");

    public static async Task RunAsync(string connection, TextWriter output, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output);

        var services = new ServiceCollection();
        services.AddDirectOrdersModule(connection);
        services.AddMappedQuotesModule(connection);
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );

        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setup = setupScope.ServiceProvider.GetRequiredService<DirectOrdersContext>();
            await setup.Database.MigrateAsync(token);
            setup.Allowances.Add(DirectAllowance.Open("alpha", 100));
            setup.Orders.Add(DirectOrder.Draft("alpha", DirectId, 40));
            await setup.SaveChangesAsync(token);
        }

        await using (var directScope = provider.CreateAsyncScope())
        {
            var direct = directScope.ServiceProvider.GetRequiredService<DirectOrdersContext>();
            await using var transaction = await direct.Database.BeginTransactionAsync(token);
            (await direct.Orders.SingleAsync(row => row.Id == DirectId, token)).Submit();
            await direct.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

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
                    Id = MappedId,
                    Amount = 60,
                    Version = 1,
                }
            );
            await setup.SaveChangesAsync(token);
        }

        await using (var mappedScope = provider.CreateAsyncScope())
        {
            var mapped = mappedScope.ServiceProvider.GetRequiredService<MappedQuotesContext>();
            await using var transaction = await mapped.Database.BeginTransactionAsync(token);
            await mappedScope
                .ServiceProvider.GetRequiredService<MappedQuoteCommands>()
                .AcceptAsync(MappedId, token);
            await mapped.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        await using (var deletionScope = provider.CreateAsyncScope())
        {
            var deletion = deletionScope.ServiceProvider.GetRequiredService<DirectOrdersContext>();
            await using var transaction = await deletion.Database.BeginTransactionAsync(token);
            var root = await deletion.Orders.SingleAsync(row => row.Id == DirectId, token);
            root.Delete();
            deletion.Orders.Remove(root);
            await deletion.SaveChangesAsync(token);
            if (deletion.Entry(root).State != EntityState.Detached || root.Events.Count != 0)
                throw new InvalidOperationException(
                    "Deleted aggregate events were lost or not acknowledged."
                );

            await transaction.CommitAsync(token);
        }

        await using var directObserverScope = provider.CreateAsyncScope();
        var directRead =
            directObserverScope.ServiceProvider.GetRequiredService<DirectOrdersContext>();
        await using var mappedObserverScope = provider.CreateAsyncScope();
        var mappedRead =
            mappedObserverScope.ServiceProvider.GetRequiredService<MappedQuotesContext>();
        if (
            await directRead.Orders.AnyAsync(token)
            || (await directRead.Allowances.SingleAsync(token)).Reserved != 0
            || (await mappedRead.Allowances.SingleAsync(token)).Reserved != 60
            || await mappedRead.Receipts.CountAsync(token) != 1
            || await mappedRead.Allocations.CountAsync(token) != 1
        )
            throw new InvalidOperationException(
                "The independent consumer outcomes differ from the expected decisions."
            );

        await output.WriteLineAsync(
            "Direct order: two handlers and a newly tracked allowance event; deletion released 40."
        );
        await output.WriteLineAsync(
            "Immutable quote: explicit sink and bounded cascade; reserved=60, receipt=1, allocation=1."
        );
    }
}
