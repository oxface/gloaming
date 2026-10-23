using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.DomainEvents;

public static class DomainEventJourney
{
    public static Guid FirstOrderId { get; } = Guid.Parse("d2100000-0000-0000-0000-000000000001");
    public static Guid SecondOrderId { get; } = Guid.Parse("d2100000-0000-0000-0000-000000000002");
    public static Guid LineId { get; } = Guid.Parse("d2100000-0000-0000-0000-000000000011");

    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(output);

        await using var provider = DomainEventComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        await DomainEventComposition.SetupAsync(provider, cancellationToken);
        await InTransactionAsync(
            provider,
            async scope =>
            {
                await scope
                    .GetRequiredService<ICustomerOrderAllowances>()
                    .OpenAsync(SalesDemoSeed.AlphaCustomerId, "EUR", 100, cancellationToken);
            },
            cancellationToken
        );
        await InTransactionAsync(
            provider,
            async scope =>
            {
                await RequireChangedAsync(
                    scope
                        .GetRequiredService<IDraftOrderCommands>()
                        .CreateAsync(Request(FirstOrderId, 40), cancellationToken)
                );
                await RequireChangedAsync(
                    scope
                        .GetRequiredService<IDraftOrderCommands>()
                        .CreateAsync(Request(SecondOrderId, 70), cancellationToken)
                );
            },
            cancellationToken
        );
        await InTransactionAsync(
            provider,
            async scope =>
            {
                await RequireChangedAsync(
                    scope
                        .GetRequiredService<IDraftOrderCommands>()
                        .SubmitAsync(new(FirstOrderId, 1), cancellationToken)
                );
            },
            cancellationToken
        );

        await using (var rejected = DomainEventComposition.CreateScope(provider))
        {
            var database = rejected.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            await RequireChangedAsync(
                rejected
                    .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                    .SubmitAsync(new(SecondOrderId, 1), cancellationToken)
            );
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(
                    "The required allowance reaction should reject 110 EUR."
                );
            }
            catch (OrderAllowanceExceededException exception) when (exception.Requested == 110)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        await using (var accepted = DomainEventComposition.CreateScope(provider))
        {
            var database = accepted.ServiceProvider.GetRequiredService<SalesDbContext>();
            var commands = accepted.ServiceProvider.GetRequiredService<IDraftOrderCommands>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            await RequireChangedAsync(
                commands.ChangeLinesAsync(
                    new(SecondOrderId, 1, [new DraftOrderLineChange.Change(LineId, 1, 50)]),
                    cancellationToken
                )
            );
            await database.SaveChangesAsync(cancellationToken);
            await RequireChangedAsync(
                commands.SubmitAsync(new(SecondOrderId, 2), cancellationToken)
            );
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using var read = DomainEventComposition.CreateScope(provider);
        var allowance = await read
            .ServiceProvider.GetRequiredService<ICustomerOrderAllowances>()
            .ReadAsync(SalesDemoSeed.AlphaCustomerId, "EUR", cancellationToken);
        var second = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(SecondOrderId, cancellationToken);
        if (
            allowance is not { Reserved: 90, Ceiling: 100, Version: 3 }
            || second is not { IsSubmitted: true, Total: 50, Version: 3 }
        )
            throw new InvalidOperationException("The committed submission and allowance disagree.");

        await output.WriteLineAsync("Submitted 40.00 EUR through SaveChanges required handling.");
        await output.WriteLineAsync(
            "Rejected 70.00 EUR: combined 110.00 exceeds allowance 100.00; rolled back submission."
        );
        await output.WriteLineAsync(
            "Edited and submitted 50.00 EUR through explicit staging; reserved=90.00 EUR, allowance v3."
        );
    }

    public static CreateDraftOrder Request(Guid id, decimal amount) =>
        new(
            id,
            SalesDemoSeed.AlphaCustomerId,
            id.ToString("N"),
            "EUR",
            100,
            [new(LineId, "DEMO-NOTEBOOK", 1, amount)]
        );

    private static async Task RequireChangedAsync(Task<DraftOrderChangeResult> proposed)
    {
        if (await proposed is not DraftOrderChangeResult.Changed)
            throw new InvalidOperationException("The finite journey did not accept the proposal.");
    }

    private static async Task InTransactionAsync(
        ServiceProvider provider,
        Func<IServiceProvider, Task> action,
        CancellationToken cancellationToken
    )
    {
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        await action(scope.ServiceProvider);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
