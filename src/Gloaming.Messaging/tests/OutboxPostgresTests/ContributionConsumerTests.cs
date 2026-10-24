using Gloaming.Messaging.ContributionProof;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class ContributionConsumerTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Due = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly ContributionSetKey Key = new(
        "admitted",
        Guid.Parse("00000000-0000-0000-0000-000000000081"),
        Guid.Parse("00000000-0000-0000-0000-000000000082")
    );

    [Fact]
    public async Task ImportConsumerRequiresAllChunksAndAppliesItsOwnResultReaderOnlyAfterTechnicalCompletion()
    {
        string connection = await SetupAsync();
        await WriteImportAsync(
            connection,
            database =>
            {
                Importing.Open(database, Key, Due);
                return Task.FromResult(true);
            }
        );
        Assert.Equal(
            ContributionResult.Accepted,
            await ReceiveImportAsync(connection, "chunk-1", 10)
        );
        Assert.Equal(
            ContributionResult.Accepted,
            await ReceiveImportAsync(connection, "chunk-2", 20)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteImportAsync(
                connection,
                database => Importing.ApplyReadyAsync(database, Key, Token)
            )
        );
        Assert.Equal(
            ContributionResult.Completed,
            await ReceiveImportAsync(connection, "chunk-3", 30)
        );
        await using (var before = Imports(connection))
        {
            Assert.Equal(
                ImportState.Collecting,
                (await before.Set<ImportBatch>().SingleAsync(Token)).State
            );
            Assert.Equal(
                "imports.results-ready",
                (await before.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageName
            );
            Assert.Null((await before.Set<OutboxMessageRecord>().SingleAsync(Token)).TenantKey);
        }

        Assert.True(
            await WriteImportAsync(
                connection,
                database => Importing.ApplyReadyAsync(database, Key, Token)
            )
        );
        Assert.False(
            await WriteImportAsync(
                connection,
                database => Importing.ApplyReadyAsync(database, Key, Token)
            )
        );
        await using var read = Imports(connection);
        var batch = await read.Set<ImportBatch>().SingleAsync(Token);
        Assert.Equal(ImportState.Ready, batch.State);
        Assert.Equal(60, batch.Rows);
    }

    [Theory]
    [InlineData(10, OfferState.Rejected)]
    [InlineData(100, OfferState.Selected)]
    public async Task IndependentOfferConsumerChoosesTwoOfThreeAndOwnsBusinessAcceptance(
        int budget,
        OfferState expected
    )
    {
        string connection = await SetupAsync();
        await WriteOfferAsync(
            connection,
            database =>
            {
                Offers.Open(database, Key, budget);
                return Task.FromResult(true);
            }
        );
        Assert.Equal(ContributionResult.Accepted, await ReceiveOfferAsync(connection, "gamma", 23));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ReceiveOfferAsync(connection, "alpha", -1)
        );
        Assert.Equal(
            ContributionResult.Completed,
            await ReceiveOfferAsync(connection, "alpha", 17)
        );
        Assert.Equal(
            ContributionResult.AlreadyCompleted,
            await ReceiveOfferAsync(connection, "beta", 11)
        );
        await using (var before = OfferContext(connection))
        {
            Assert.Equal(
                ContributionSetState.Completed,
                (await before.Set<ContributionSetRecord>().SingleAsync(Token)).State
            );
            Assert.Equal(
                OfferState.Collecting,
                (await before.Set<OfferRequest>().SingleAsync(Token)).State
            );
            Assert.Equal(
                Key.ScopeKey,
                (await before.Set<OutboxMessageRecord>().SingleAsync(Token)).TenantKey
            );
        }

        Assert.Equal(
            expected,
            await WriteOfferAsync(
                connection,
                database => Offers.ApplyReadyAsync(database, Key, Token)
            )
        );
        Assert.Equal(
            expected,
            await WriteOfferAsync(
                connection,
                database => Offers.ApplyReadyAsync(database, Key, Token)
            )
        );
        await using var read = OfferContext(connection);
        var request = await read.Set<OfferRequest>().SingleAsync(Token);
        Assert.Equal(expected == OfferState.Selected ? 17m : (decimal?)null, request.SelectedPrice);
        Assert.Equal(2, await read.Set<ContributionRecord>().CountAsync(Token));
        Assert.Equal(1, await read.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task IdenticalTechnicalKeysInSeparateConsumerSchemasKeepTheirOwnPolicyAndEvidence()
    {
        string connection = await SetupAsync();
        await WriteImportAsync(
            connection,
            database =>
            {
                Importing.Open(database, Key, Due);
                return Task.FromResult(true);
            }
        );
        await WriteOfferAsync(
            connection,
            database =>
            {
                Offers.Open(database, Key, 100);
                return Task.FromResult(true);
            }
        );
        await ReceiveImportAsync(connection, "chunk-1", 10);
        await ReceiveOfferAsync(connection, "alpha", 17);
        await ReceiveOfferAsync(connection, "beta", 23);
        await using var imports = Imports(connection);
        await using var offers = OfferContext(connection);
        Assert.Equal(
            ContributionSetState.Open,
            (await imports.Set<ContributionSetRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(
            ContributionSetState.Completed,
            (await offers.Set<ContributionSetRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(0, await imports.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(1, await offers.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData("staged", false)]
    [InlineData("saved", false)]
    [InlineData("committed", true)]
    public async Task ImportCompletionSurvivesOwnProcessDeathWithStableRetainedNotice(
        string phase,
        bool committed
    )
    {
        string connection = await OpenImportWithTwoChunksAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await using (
            var child = new ChildHost(
                typeof(Application).Assembly,
                new() { ["CONTRIBUTION_CONNECTION"] = connection, ["CONTRIBUTION_PAUSE"] = phase },
                "arrive",
                Key.ScopeKey,
                Key.WorkId.ToString(),
                Key.GenerationId.ToString(),
                "chunk-3",
                "30"
            )
        )
        {
            await child.WaitForOutputAsync("PAUSED:" + phase, deadline.Token);
            await child.KillAsync(deadline.Token);
        }

        await using (var read = Imports(connection))
        {
            Assert.Equal(
                committed ? ContributionSetState.Completed : ContributionSetState.Open,
                (await read.Set<ContributionSetRecord>().SingleAsync(Token)).State
            );
            Assert.Equal(committed ? 3 : 2, await read.Set<ContributionRecord>().CountAsync(Token));
            Assert.Equal(
                committed ? 1 : 0,
                await read.Set<OutboxMessageRecord>().CountAsync(Token)
            );
            Assert.Equal(
                committed ? DueIntentState.Cancelled : DueIntentState.Pending,
                (await read.Set<DueIntentRecord>().SingleAsync(Token)).State
            );
        }

        Assert.Equal(
            committed ? ContributionResult.Duplicate : ContributionResult.Completed,
            await ReceiveImportAsync(connection, "chunk-3", 30)
        );
        Assert.True(
            await WriteImportAsync(
                connection,
                database => Importing.ApplyReadyAsync(database, Key, Token)
            )
        );
        await using var recovered = Imports(connection);
        Assert.Equal(60, (await recovered.Set<ImportBatch>().SingleAsync(Token)).Rows);
        Assert.Equal(1, await recovered.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData("saved", false)]
    [InlineData("committed", true)]
    public async Task TimeoutProcessRecoveryKeepsConsumerExpirySeparateFromTechnicalClosure(
        string phase,
        bool committed
    )
    {
        string connection = await OpenImportWithTwoChunksAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await using (
            var child = new ChildHost(
                typeof(Application).Assembly,
                new() { ["CONTRIBUTION_CONNECTION"] = connection, ["CONTRIBUTION_PAUSE"] = phase },
                "timeout",
                Key.ScopeKey,
                Key.WorkId.ToString(),
                Key.GenerationId.ToString(),
                Due.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            )
        )
        {
            await child.WaitForOutputAsync("PAUSED:" + phase, deadline.Token);
            await child.KillAsync(deadline.Token);
        }

        Assert.Equal(
            committed
                ? ContributionTimeoutResult.AlreadyTimedOut
                : ContributionTimeoutResult.TimedOut,
            await WriteImportAsync(
                connection,
                database => Importing.TimeoutAsync(database, Key, Due, Token)
            )
        );
        await using (var before = Imports(connection))
            Assert.Equal(
                ImportState.Collecting,
                (await before.Set<ImportBatch>().SingleAsync(Token)).State
            );

        Assert.True(
            await WriteImportAsync(
                connection,
                database => Importing.ApplyExpiredAsync(database, Key, Token)
            )
        );
        Assert.False(
            await WriteImportAsync(
                connection,
                database => Importing.ApplyExpiredAsync(database, Key, Token)
            )
        );
        await using var read = Imports(connection);
        Assert.Equal(ImportState.Expired, (await read.Set<ImportBatch>().SingleAsync(Token)).State);
        Assert.Equal(2, await read.Set<ContributionRecord>().CountAsync(Token));
        Assert.Equal(
            "imports.results-late",
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageName
        );
    }

    [Fact]
    public async Task ConsumerCurrentGenerationRejectsStaleContributionsBeforeTechnicalWork()
    {
        string connection = await OpenImportWithTwoChunksAsync();
        await WriteImportAsync(
            connection,
            async database =>
            {
                var batch = await database.Set<ImportBatch>().SingleAsync(Token);
                batch.GenerationId = Guid.NewGuid();
                return true;
            }
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReceiveImportAsync(connection, "chunk-3", 30)
        );
        await using var read = Imports(connection);
        Assert.Equal(
            ContributionSetState.Open,
            (await read.Set<ContributionSetRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(2, await read.Set<ContributionRecord>().CountAsync(Token));
        Assert.Equal(0, await read.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 99)]
    [InlineData(true, 0)]
    [InlineData(true, 99)]
    public async Task ConsumerReadersRejectUndefinedStoredBusinessStates(bool offer, int state)
    {
        string connection = await SetupAsync();
        if (offer)
        {
            await WriteOfferAsync(
                connection,
                database =>
                {
                    Offers.Open(database, Key, 100);
                    return Task.FromResult(true);
                }
            );
            await using (var corrupt = OfferContext(connection))
                await corrupt.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE offers.requests SET \"State\" = {state}",
                    Token
                );

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                WriteOfferAsync(
                    connection,
                    database => Offers.ApplyReadyAsync(database, Key, Token)
                )
            );
        }
        else
        {
            await WriteImportAsync(
                connection,
                database =>
                {
                    Importing.Open(database, Key, Due);
                    return Task.FromResult(true);
                }
            );
            await using (var corrupt = Imports(connection))
                await corrupt.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE imports.batches SET \"State\" = {state}",
                    Token
                );

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                WriteImportAsync(
                    connection,
                    database => Importing.ApplyReadyAsync(database, Key, Token)
                )
            );
        }
    }

    private async Task<string> OpenImportWithTwoChunksAsync()
    {
        string connection = await SetupAsync();
        await WriteImportAsync(
            connection,
            database =>
            {
                Importing.Open(database, Key, Due);
                return Task.FromResult(true);
            }
        );
        await ReceiveImportAsync(connection, "chunk-1", 10);
        await ReceiveImportAsync(connection, "chunk-2", 20);
        return connection;
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var imports = Imports(connection);
        await imports.Database.EnsureCreatedAsync(Token);
        await using var offers = OfferContext(connection);
        await offers.Database.ExecuteSqlRawAsync(offers.Database.GenerateCreateScript(), Token);
        return connection;
    }

    private static ImportDatabase Imports(string connection) =>
        new(new DbContextOptionsBuilder<ImportDatabase>().UseNpgsql(connection).Options);

    private static OfferDatabase OfferContext(string connection) =>
        new(new DbContextOptionsBuilder<OfferDatabase>().UseNpgsql(connection).Options);

    private static Task<ContributionResult> ReceiveImportAsync(
        string connection,
        string chunk,
        int rows
    ) =>
        WriteImportAsync(
            connection,
            database => Importing.ReceiveAsync(database, Key, chunk, rows, Token)
        );

    private static Task<ContributionResult> ReceiveOfferAsync(
        string connection,
        string vendor,
        decimal price
    ) =>
        WriteOfferAsync(
            connection,
            database => Offers.ReceiveAsync(database, Key, vendor, price, Token)
        );

    private static async Task<T> WriteImportAsync<T>(
        string connection,
        Func<ImportDatabase, Task<T>> operation
    )
    {
        await using var database = Imports(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        T result = await operation(database);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static async Task<T> WriteOfferAsync<T>(
        string connection,
        Func<OfferDatabase, Task<T>> operation
    )
    {
        await using var database = OfferContext(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        T result = await operation(database);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }
}
