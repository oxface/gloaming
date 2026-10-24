using System.Globalization;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.ContributionProof;

public static class Application
{
    public static async Task Main(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("CONTRIBUTION_CONNECTION")
            ?? throw new InvalidOperationException("Select a disposable contribution database.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var key = new ContributionSetKey(args[1], Guid.Parse(args[2]), Guid.Parse(args[3]));
        await using var database = new ImportDatabase(
            new DbContextOptionsBuilder<ImportDatabase>().UseNpgsql(connection).Options
        );
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        if (args[0] == "timeout")
            Console.WriteLine(
                "RESULT:"
                    + await Importing.TimeoutAsync(
                        database,
                        key,
                        DateTimeOffset.Parse(args[4], CultureInfo.InvariantCulture),
                        token
                    )
            );
        else
            Console.WriteLine(
                "RESULT:"
                    + await Importing.ReceiveAsync(
                        database,
                        key,
                        args[4],
                        int.Parse(args[5], CultureInfo.InvariantCulture),
                        token
                    )
            );

        await PauseAsync("staged", token);
        await database.SaveChangesAsync(token);
        Console.WriteLine("SAVED");
        await PauseAsync("saved", token);
        await transaction.CommitAsync(token);
        Console.WriteLine("COMMITTED");
        await PauseAsync("committed", token);
    }

    private static async Task PauseAsync(string phase, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("CONTRIBUTION_PAUSE") != phase)
            return;

        Console.WriteLine("PAUSED:" + phase);
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
}
