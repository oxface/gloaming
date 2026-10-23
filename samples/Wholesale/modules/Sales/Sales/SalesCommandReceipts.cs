using System.Text.Json;
using System.Text.Json.Serialization;
using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales;

// Sales owns its payload meanings/readers and translates the technical conflict at its Contract boundary.
internal static class SalesCommandReceipts
{
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters =
        {
            new JsonStringEnumConverter<StockReservationOrderStatus>(allowIntegerValues: false),
        },
    };

    internal static string Input<T>(T value) => JsonSerializer.Serialize(value, Json);

    internal static async Task<SalesCommandReceipt<TResult>> AcquireAsync<TResult>(
        SalesDbContext database,
        string operation,
        Guid operationId,
        string input,
        CancellationToken cancellationToken
    )
        where TResult : class
    {
        try
        {
            var attempt = await new PostgresCommandReceipts<SalesDbContext>(database).AcquireAsync(
                new(database.RequiredOrganizationKey, operation, operationId),
                input,
                cancellationToken
            );
            return new(attempt);
        }
        catch (Gloaming.CommandReceipts.EntityFrameworkCore.Postgres.CommandReceiptConflictException)
        {
            throw new Contracts.CommandReceiptConflictException();
        }
    }

    internal sealed class SalesCommandReceipt<TResult>
        where TResult : class
    {
        private readonly CommandReceiptAttempt attempt;

        internal SalesCommandReceipt(CommandReceiptAttempt attempt)
        {
            this.attempt = attempt;
            if (attempt.Result is not { } retained)
                return;

            if (retained.FormatVersion != 1)
                throw new InvalidOperationException(
                    "Unsupported Sales command receipt format; use a compatible reader."
                );

            Result =
                JsonSerializer.Deserialize<TResult>(retained.Payload, Json)
                ?? throw new InvalidOperationException(
                    "A Sales command receipt requires a retained result."
                );
        }

        internal TResult? Result { get; }

        internal void Retain(TResult result) =>
            attempt.Retain(new(1, JsonSerializer.Serialize(result, Json)));
    }
}
