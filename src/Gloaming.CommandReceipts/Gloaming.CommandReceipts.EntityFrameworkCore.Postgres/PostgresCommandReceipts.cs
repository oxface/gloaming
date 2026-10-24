using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>Cooperating logical-command receipt writers over one explicit native PostgreSQL context and transaction.</summary>
public sealed class PostgresCommandReceipts<TContext>
    where TContext : DbContext
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly TContext database;

    /// <summary>Binds callable receipt operations to one consumer-owned context.</summary>
    public PostgresCommandReceipts(TContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        this.database = database;
    }

    /// <summary>Waits for a same-key attempt, compares retained input ordinally, and returns the original result or a new attempt. Caller owns authorization, save and commit.</summary>
    public async Task<CommandReceiptAttempt> AcquireAsync(
        CommandReceiptKey key,
        string input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("PostgreSQL text cannot contain NUL.", nameof(input));

        var scope = ReceiptScope.For(database);
        try
        {
            scope.RequireTransaction(database);
            if (!database.Database.IsNpgsql() || scope.Acquired)
                throw new InvalidOperationException(
                    "Use PostgreSQL and one acquired command per fresh context scope."
                );

            var entity = database.Model.FindEntityType(typeof(CommandReceiptRecord));
            if (
                entity?.FindAnnotation(CommandReceiptConfiguration.Capability)?.Value is not true
                || entity.GetSchema() is not { } schema
                || entity.GetTableName() is not { } table
                || entity.GetProperties().Count() != 6
                || entity.FindPrimaryKey() is not { } primaryKey
                || !primaryKey
                    .Properties.Select(property => property.Name)
                    .SequenceEqual(
                        new[]
                        {
                            nameof(CommandReceiptRecord.ScopeKey),
                            nameof(CommandReceiptRecord.Operation),
                            nameof(CommandReceiptRecord.OperationId),
                        }
                    )
            )
                throw new InvalidOperationException(
                    "Configure the provided single-table receipt record with its complete key."
                );

            CommandReceiptConfiguration.ValidateIdentifier(schema);
            CommandReceiptConfiguration.ValidateIdentifier(table);
            scope.Acquired = true;
            scope.Transaction = database.Database.CurrentTransaction;
            long lockKey = LockKey(schema, table, key);
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                cancellationToken
            );
            var retained = await database
                .Set<CommandReceiptRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row =>
                        row.ScopeKey == key.ScopeKey
                        && row.Operation == key.Operation
                        && row.OperationId == key.OperationId,
                    cancellationToken
                );
            if (
                retained is not null
                && !string.Equals(retained.Input, input, StringComparison.Ordinal)
            )
                throw new CommandReceiptConflictException();

            return new(
                database,
                scope,
                key,
                input,
                retained is null ? null : new(retained.FormatVersion, retained.Result)
            );
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    // Protocol v1: fixed prefix, then five UTF-8 strings with signed 32-bit BE byte lengths.
    // GUID is lowercase D text. Interpret the first eight SHA-256 bytes as a signed BE integer.
    private static long LockKey(string schema, string table, CommandReceiptKey key)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("Gloaming.CommandReceipts/PostgreSQL/v1\0"u8);
        Span<byte> length = stackalloc byte[4];
        foreach (
            string field in new[]
            {
                schema,
                table,
                key.ScopeKey,
                key.Operation,
                key.OperationId.ToString("D"),
            }
        )
        {
            byte[] bytes = Utf8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return BinaryPrimitives.ReadInt64BigEndian(hash.GetHashAndReset());
    }
}
