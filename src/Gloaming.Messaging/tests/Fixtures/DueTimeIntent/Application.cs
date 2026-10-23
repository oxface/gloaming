using System.Globalization;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

#if EARLIER_BUILD
namespace Gloaming.Messaging.DueTimeProof.Earlier;

#else
namespace Gloaming.Messaging.DueTimeProof.Current;

#endif

public static class Application
{
    public static async Task<int> Main(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("DUE_INTENT_CONNECTION")
            ?? throw new InvalidOperationException("A disposable connection is required.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        string role = args[0];
        string scope = args[1];
        DateTimeOffset now = DateTimeOffset.Parse(args[2], CultureInfo.InvariantCulture);
        await using DbContext database = role.EndsWith("personnel", StringComparison.Ordinal)
            ? new PersonnelDatabase(
                new DbContextOptionsBuilder<PersonnelDatabase>().UseNpgsql(connection).Options
            )
            : new GrowthDatabase(
                new DbContextOptionsBuilder<GrowthDatabase>().UseNpgsql(connection).Options
            );
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        try
        {
            if (role.StartsWith("release-", StringComparison.Ordinal))
            {
                Guid[] released = await NativeDueIntents.ReleaseAsync(
                    database,
                    scope,
                    now,
                    1,
                    token
                );
                Console.WriteLine("STAGED:" + released.Length);
                await PauseAsync("staged", token);
            }
            else if (role.StartsWith("receive-", StringComparison.Ordinal))
            {
                Guid id = Guid.Parse(args[3]);
                var row = await database
                    .Set<OutboxMessageRecord>()
                    .AsNoTracking()
                    .SingleAsync(value => value.MessageId == id && value.TenantKey == scope, token);
                var message = new OutgoingMessage(
                    row.MessageId,
                    row.RouteKey,
                    row.MessageName,
                    row.SchemaVersion,
                    row.Payload,
                    row.TenantKey,
                    row.CorrelationId
                );
                var result = database is PersonnelDatabase personnel
                    ? await Personnel.AcceptAsync(personnel, scope, message, now, token)
                    : await Growth.AcceptAsync(
                        (GrowthDatabase)database,
                        scope,
                        message,
                        now,
                        token
                    );
                Console.WriteLine("DECISION:" + result);
                if (result == Acceptance.NotDue)
                    return 12;
            }
            else
                throw new InvalidOperationException("Select a finite proof role.");

            await database.SaveChangesAsync(token);
            Console.WriteLine("SAVED");
            await PauseAsync("saved", token);
            await transaction.CommitAsync(token);
            Console.WriteLine("COMMITTED");
            await PauseAsync("committed", token);
            return 0;
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("INCOMPATIBLE_READER");
            return 10;
        }
    }

    private static async Task PauseAsync(string phase, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("DUE_INTENT_PAUSE") != phase)
            return;

        Console.WriteLine("PAUSED:" + phase);
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
}
