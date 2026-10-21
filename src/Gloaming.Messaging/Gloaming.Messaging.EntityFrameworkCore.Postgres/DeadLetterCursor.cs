using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

// Shared position encoding only; each store owns its lane/filter scope and lifecycle queries.
internal sealed record DeadLetterCursor(
    int Version,
    string Scope,
    DateTimeOffset DeadLetteredAt,
    Guid MessageId
)
{
    internal string Encode() =>
        Convert
            .ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    internal static DeadLetterCursor Decode(string token, string scope)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024)
            throw new ArgumentException(
                "Use a continuation from this exact dead-letter query.",
                nameof(token)
            );

        try
        {
            string base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var cursor = JsonSerializer.Deserialize<DeadLetterCursor>(
                Convert.FromBase64String(base64)
            );
            if (
                cursor is null
                || cursor.Version != 1
                || cursor.Scope != scope
                || cursor.MessageId == Guid.Empty
                || cursor.DeadLetteredAt.Offset != TimeSpan.Zero
            )
                throw new FormatException(
                    "The continuation has an invalid version, scope or position."
                );

            return cursor;
        }
        catch (Exception failure) when (failure is FormatException or JsonException)
        {
            throw new ArgumentException(
                "Use a continuation from this exact dead-letter query.",
                nameof(token),
                failure
            );
        }
    }
}
