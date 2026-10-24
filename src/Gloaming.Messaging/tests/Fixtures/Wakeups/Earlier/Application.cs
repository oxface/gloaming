using Npgsql;

namespace Gloaming.Messaging.WakeupEarlier;

// Native writer compiled without Gloaming or any notification configuration.
public static class Application
{
    public static async Task Main(string[] args)
    {
        string endpoint =
            Environment.GetEnvironmentVariable("WAKEUP_CONNECTION")
            ?? throw new InvalidOperationException("Select a disposable queue database.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(endpoint);
        await connection.OpenAsync(deadline.Token);
        await using var transaction = await connection.BeginTransactionAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO waking.inbox(subscription_key, producer_key, message_id, message_name, schema_version, payload)
                VALUES('selected', 'earlier', @id, 'owned.work', 1, '{"value":7}');
            INSERT INTO waking.outbox(message_id, destination, message_name, schema_version, payload, attempts)
                VALUES(@id, 'selected', 'owned.work', 1, '{"value":7}', 0);
            """,
            connection,
            transaction
        );
        command.Parameters.AddWithValue("id", Guid.Parse(args[0]));
        await command.ExecuteNonQueryAsync(deadline.Token);
        await transaction.CommitAsync(deadline.Token);
    }
}
