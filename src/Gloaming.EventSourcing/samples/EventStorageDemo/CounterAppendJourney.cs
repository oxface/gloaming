using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventStorageDemo;

public static class CounterAppendJourney
{
    public static readonly Guid Id = Guid.Parse("e5010000-0000-0000-0000-000000000003");

    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var options = StorageDbContext.Options(connection);
        await using (var create = new StorageDbContext(options))
        {
            if (
                await new CounterCommands(create, new CounterClock()).ReadAsync(
                    Id,
                    cancellationToken
                )
                is null
            )
            {
                await using var transaction = await create.Database.BeginTransactionAsync(
                    cancellationToken
                );
                try
                {
                    var result = await new CounterCommands(
                        create,
                        new CounterClock()
                    ).StageStartAsync(Id, 10, cancellationToken);
                    if (result is not CounterChangeResult.Changed)
                        throw new InvalidOperationException("Counter creation did not stage.");
                    await create.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
        }
        await using (var edit = new StorageDbContext(options))
        {
            var commands = new CounterCommands(edit, new CounterClock());
            var current = (await commands.ReadAsync(Id, cancellationToken))!;
            if (current.Version == 1)
            {
                await using var transaction = await edit.Database.BeginTransactionAsync(
                    cancellationToken
                );
                try
                {
                    var result = await commands.StageIncreaseAsync(
                        Id,
                        current.Version,
                        [7, 5],
                        cancellationToken
                    );
                    if (result is not CounterChangeResult.Changed)
                        throw new InvalidOperationException("Counter increases did not stage.");
                    await edit.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
        }
        await using var read = new StorageDbContext(options);
        var committed = (
            await new CounterCommands(read, new CounterClock()).ReadAsync(Id, cancellationToken)
        )!;
        await using var rejection = await read.Database.BeginTransactionAsync(cancellationToken);
        var rejected = await new CounterCommands(read, new CounterClock()).StageIncreaseAsync(
            Id,
            committed.Version,
            [4],
            cancellationToken
        );
        if (rejected is not CounterChangeResult.LimitExceeded)
            throw new InvalidOperationException("The counter ceiling did not reject the decision.");
        await rejection.RollbackAsync(cancellationToken);
        await output.WriteLineAsync(
            $"journal append: version={committed.Version}, value={committed.Value}, rejected=4"
        );
    }
}
