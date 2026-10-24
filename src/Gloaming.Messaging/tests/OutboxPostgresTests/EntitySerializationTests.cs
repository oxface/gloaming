using Gloaming.Messaging.EntityWorkProof;
using Gloaming.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class EntitySerializationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeparateWorkersExcludeOneCompleteEntityUntilNativeCommit()
    {
        string connection = await SetupAsync();
        string release = ReleasePath();
        try
        {
            await using var first = Start(
                connection,
                "importing",
                "Tenant",
                "résumé",
                "hold",
                release
            );
            await first.WaitForOutputAsync("SQL_SAVED", Token);
            Assert.Contains(
                "LOCK_KEY:-2143320287535571979",
                first.Output,
                StringComparison.Ordinal
            );
            await using var second = Start(connection, "importing", "Tenant", "résumé", "commit");
            await WaitForAdvisoryWaitAsync(connection);
            Assert.DoesNotContain("ACQUIRED", second.Output, StringComparison.Ordinal);
            await File.WriteAllTextAsync(release, "release", Token);
            Assert.Equal(0, await first.WaitForExitAsync(Token));
            Assert.Equal(0, await second.WaitForExitAsync(Token));
            Assert.Contains("COMMITTED", second.Output, StringComparison.Ordinal);
            Assert.Equal(2, await TotalAsync(connection));
        }
        finally
        {
            File.Delete(release);
        }
    }

    [Theory]
    [InlineData("billing", "Tenant", "résumé")]
    [InlineData("importing", "tenant", "résumé")]
    [InlineData("importing", "Tenant", "Résumé")]
    [InlineData("importing", "Tenant", "another-entity")]
    public async Task OtherModulesScopesAndCompleteEntityKeysProceedDuringAnUnrelatedLock(
        string module,
        string scope,
        string entity
    )
    {
        string connection = await SetupAsync();
        string release = ReleasePath();
        try
        {
            await using var first = Start(
                connection,
                "importing",
                "Tenant",
                "résumé",
                "hold",
                release
            );
            await first.WaitForOutputAsync("SQL_SAVED", Token);
            await using var second = Start(connection, module, scope, entity, "commit");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            Assert.Equal(0, await second.WaitForExitAsync(deadline.Token));
            Assert.DoesNotContain("COMMITTED", first.Output, StringComparison.Ordinal);
            Assert.Equal(1, await TotalAsync(connection));
            await File.WriteAllTextAsync(release, "release", Token);
            Assert.Equal(0, await first.WaitForExitAsync(Token));
            Assert.Equal(2, await TotalAsync(connection));
        }
        finally
        {
            File.Delete(release);
        }
    }

    [Fact]
    public async Task ProcessDeathRollsBackItsEffectAndReleasesTheWaitingEntityWorker()
    {
        string connection = await SetupAsync();
        await using var first = Start(
            connection,
            "importing",
            "Tenant",
            "résumé",
            "hold",
            ReleasePath()
        );
        await first.WaitForOutputAsync("SQL_SAVED", Token);
        await using var second = Start(connection, "importing", "Tenant", "résumé", "commit");
        await WaitForAdvisoryWaitAsync(connection);
        await first.KillAsync(Token);
        Assert.Equal(0, await second.WaitForExitAsync(Token));
        Assert.Equal(1, await TotalAsync(connection));
    }

    [Fact]
    public async Task NativeRollbackReleasesExclusionWithoutACommittedEffect()
    {
        string connection = await SetupAsync();
        await using (var first = Start(connection, "billing", "Tenant", "invoice", "rollback"))
        {
            Assert.Equal(0, await first.WaitForExitAsync(Token));
            Assert.Contains("ROLLED_BACK", first.Output, StringComparison.Ordinal);
        }

        Assert.Equal(0, await TotalAsync(connection));
        await using var second = Start(connection, "billing", "Tenant", "invoice", "commit");
        Assert.Equal(0, await second.WaitForExitAsync(Token));
        Assert.Equal(1, await TotalAsync(connection));
    }

    [Fact]
    public async Task CancellationOfABlockedNativeAcquireReleasesResourcesForFreshRecovery()
    {
        string connection = await SetupAsync();
        string release = ReleasePath();
        try
        {
            await using var first = Start(
                connection,
                "billing",
                "Tenant",
                "invoice",
                "hold",
                release
            );
            await first.WaitForOutputAsync("SQL_SAVED", Token);
            await using (var second = Start(connection, "billing", "Tenant", "invoice", "commit"))
            {
                await WaitForAdvisoryWaitAsync(connection);
                await second.SignalAsync("-TERM", Token);
                Assert.Equal(0, await second.WaitForExitAsync(Token));
                Assert.Contains("CANCELLED", second.Output, StringComparison.Ordinal);
                Assert.DoesNotContain("ACQUIRED", second.Output, StringComparison.Ordinal);
            }

            Assert.Equal(0, await TotalAsync(connection));
            await File.WriteAllTextAsync(release, "release", Token);
            Assert.Equal(0, await first.WaitForExitAsync(Token));
            await using var recovery = Start(connection, "billing", "Tenant", "invoice", "commit");
            Assert.Equal(0, await recovery.WaitForExitAsync(Token));
            Assert.Equal(2, await TotalAsync(connection));
        }
        finally
        {
            File.Delete(release);
        }
    }

    [Fact]
    public async Task TransactionCompletionReleasesTheLockWhileTheOwningProcessRemainsAlive()
    {
        string connection = await SetupAsync();
        await using var first = Start(
            connection,
            "importing",
            "Tenant",
            "résumé",
            "committed-hold"
        );
        await first.WaitForOutputAsync("COMMITTED", Token);
        await using var second = Start(connection, "importing", "Tenant", "résumé", "commit");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        Assert.Equal(0, await second.WaitForExitAsync(deadline.Token));
        Assert.Equal(2, await TotalAsync(connection));
        await first.KillAsync(Token);
    }

    [Fact]
    public async Task OrdinaryRestrictedModuleCredentialsCanUseTheNativeProtocolWithoutSchemaOrRetentionAuthority()
    {
        string connection = await SetupAsync();
        string role = "gloaming_entity_worker_" + Guid.NewGuid().ToString("N");
        string password = Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(connection);
        await owner.OpenAsync(Token);
        await using (
            var provision = new NpgsqlCommand(
                $"""
                CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                GRANT USAGE ON SCHEMA entity_work TO "{role}";
                GRANT SELECT, INSERT, UPDATE ON entity_work.counters TO "{role}";
                """,
                owner
            )
        )
            await provision.ExecuteNonQueryAsync(Token);

        Exception? originalFailure = null;
        try
        {
            string restricted = new NpgsqlConnectionStringBuilder(connection)
            {
                Username = role,
                Password = password,
                Pooling = false,
            }.ConnectionString;
            await using var runtime = new NpgsqlConnection(restricted);
            await runtime.OpenAsync(Token);
            await using (
                var audit = new NpgsqlCommand(
                    """
                    SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                        AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                        AND NOT has_schema_privilege(current_user, 'entity_work', 'CREATE')
                        AND NOT has_table_privilege(current_user, 'entity_work.counters', 'DELETE')
                    FROM pg_roles r WHERE rolname = current_user
                    """,
                    runtime
                )
            )
                Assert.Equal(true, await audit.ExecuteScalarAsync(Token));

            await using var worker = Start(restricted, "billing", "Tenant", "invoice", "commit");
            Assert.Equal(0, await worker.WaitForExitAsync(Token));
            Assert.Equal(1, await TotalAsync(connection));
            await using var denied = new NpgsqlCommand("DELETE FROM entity_work.counters", runtime);
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                denied.ExecuteNonQueryAsync(Token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }
        catch (Exception error)
        {
            originalFailure = error;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await using var remove = new NpgsqlCommand(
                    $"DROP OWNED BY \"{role}\"; DROP ROLE \"{role}\"",
                    owner
                );
                await remove.ExecuteNonQueryAsync(cleanup.Token);
            }
            catch (Exception error) when (originalFailure is not null)
            {
                Console.Error.WriteLine(
                    $"Entity-work role cleanup failed ({error.GetType().Name})."
                );
            }
        }
    }

    [Fact]
    public async Task LengthFramingKeepsDelimiterBearingCompleteKeysIndependent()
    {
        string connection = await SetupAsync();
        string release = ReleasePath();
        try
        {
            await using var first = Start(connection, "importing", "a:b", "c", "hold", release);
            await first.WaitForOutputAsync("SQL_SAVED", Token);
            await using var second = Start(connection, "importing", "a", "b:c", "commit");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            Assert.Equal(0, await second.WaitForExitAsync(deadline.Token));
            Assert.DoesNotContain("COMMITTED", first.Output, StringComparison.Ordinal);
            await File.WriteAllTextAsync(release, "release", Token);
            Assert.Equal(0, await first.WaitForExitAsync(Token));
            Assert.Equal(2, await TotalAsync(connection));
        }
        finally
        {
            File.Delete(release);
        }
    }

    [Fact]
    public async Task RollingBackBeforeLockAcquisitionReleasesExclusionEvenWhileTheTransactionRemainsOpen()
    {
        string connection = await SetupAsync();
        await using var first = Start(connection, "billing", "Tenant", "invoice", "savepoint-hold");
        await first.WaitForOutputAsync("SAVEPOINT_REVERTED", Token);
        await using var second = Start(connection, "billing", "Tenant", "invoice", "commit");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        Assert.Equal(0, await second.WaitForExitAsync(deadline.Token));
        Assert.Equal(1, await TotalAsync(connection));
        Assert.DoesNotContain("COMMITTED", first.Output, StringComparison.Ordinal);
        await first.KillAsync(Token);
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var setup = new NpgsqlCommand(
            """
            CREATE SCHEMA entity_work;
            CREATE TABLE entity_work.counters (
                module text COLLATE "C" NOT NULL, scope text COLLATE "C" NOT NULL,
                entity text COLLATE "C" NOT NULL, value integer NOT NULL,
                PRIMARY KEY (module, scope, entity));
            """,
            database
        );
        await setup.ExecuteNonQueryAsync(Token);
        return connection;
    }

    private static ChildHost Start(
        string connection,
        string module,
        string scope,
        string entity,
        string mode,
        string? release = null
    ) =>
        new(
            typeof(Application).Assembly,
            new()
            {
                ["ENTITY_WORK_CONNECTION"] = connection,
                ["ENTITY_WORK_RELEASE"] = release ?? "",
            },
            module,
            scope,
            entity,
            mode
        );

    private static string ReleasePath() =>
        Path.Combine(Path.GetTempPath(), "gloaming-entity-release-" + Guid.NewGuid().ToString("N"));

    private static async Task<int> TotalAsync(string connection)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var count = new NpgsqlCommand(
            "SELECT COALESCE(sum(value), 0)::int FROM entity_work.counters",
            database
        );
        return (int)(await count.ExecuteScalarAsync(Token))!;
    }

    private static async Task WaitForAdvisoryWaitAsync(string connection)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid
            WHERE a.datname = current_database() AND l.locktype = 'advisory' AND NOT l.granted
            """,
            observer
        );
        while ((long)(await command.ExecuteScalarAsync(deadline.Token))! == 0)
            await Task.Delay(25, deadline.Token);
    }
}
