using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs.TenantIsolation;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Tenancy;
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Queries;

public sealed partial class TenantIsolationTests
{
    [Fact]
    public async Task PhysicalPoolReusesAlphaThenBetaThenMissingWithoutTenantLeakage()
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        int? backend = null;
        foreach (string tenant in new[] { "wholesale-alpha", "wholesale-beta" })
        {
            await using var scope = TenantIsolationJourney.CreateScope(
                provider,
                tenant,
                "application-alpha"
            );
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var connection = (NpgsqlConnection)database.Database.GetDbConnection();
            var native = (NpgsqlTransaction)transaction.GetDbTransaction();
            int current = await ValueAsync<int>(connection, native, "SELECT pg_backend_pid()");
            if (backend is not null)
                Assert.Equal(backend, current);

            backend = current;
            Assert.Equal(0, await CountAsync(connection, native));
            await database.EstablishTenantDatabaseContextAsync(
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                "app.tenant_key",
                Token
            );
            Assert.Equal(
                tenant,
                await ValueAsync<string>(
                    connection,
                    native,
                    "SELECT organization_key FROM sales.draft_orders"
                )
            );
            if (tenant == "wholesale-alpha")
                await transaction.CommitAsync(Token);
            else
                await transaction.RollbackAsync(Token);
        }

        await using var missing = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(backend, await ValueAsync<int>(missing, null, "SELECT pg_backend_pid()"));
        Assert.Equal(0, await CountAsync(missing));
        Assert.True(
            await ValueAsync<bool>(
                missing,
                null,
                "SELECT NULLIF(current_setting('app.tenant_key', true), '') IS NULL"
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrAfterSetupRollsBackAndReturnsCleanPhysicalLease(
        bool afterSetup
    )
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        int backend;
        await using (
            var scope = TenantIsolationJourney.CreateScope(
                provider,
                "wholesale-alpha",
                "application-alpha"
            )
        )
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var connection = (NpgsqlConnection)database.Database.GetDbConnection();
            var native = (NpgsqlTransaction)transaction.GetDbTransaction();
            backend = await ValueAsync<int>(connection, native, "SELECT pg_backend_pid()");
            var tenancy = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            if (afterSetup)
            {
                await database.EstablishTenantDatabaseContextAsync(
                    tenancy,
                    "app.tenant_key",
                    Token
                );
                await CommandAsync(
                    connection,
                    native,
                    """
                    INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
                    VALUES ('a2200000-0000-0000-0000-000000000099','wholesale-alpha','CANCELLED','Cancelled',1)
                    """
                );
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            await cancellation.CancelAsync();
            if (afterSetup)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    database.Database.ExecuteSqlRawAsync("SELECT pg_sleep(10)", cancellation.Token)
                );
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    database.EstablishTenantDatabaseContextAsync(
                        tenancy,
                        "app.tenant_key",
                        cancellation.Token
                    )
                );

            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var next = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(backend, await ValueAsync<int>(next, null, "SELECT pg_backend_pid()"));
        Assert.Equal(0, await CountAsync(next));
        await using var check = await next.BeginTransactionAsync(Token);
        await SelectTenantAsync(next, check, "wholesale-alpha");
        Assert.Equal(
            0,
            await ValueAsync<int>(
                next,
                check,
                "SELECT count(*) FROM sales.customer_profiles WHERE code='CANCELLED'"
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavepointRollbackBeforeSetupRemovesItAndAfterSetupRetainsIt(bool setupFirst)
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        await using var scope = TenantIsolationJourney.CreateScope(
            provider,
            "wholesale-alpha",
            "application-alpha"
        );
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var tenancy = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        if (setupFirst)
            await database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", Token);

        await transaction.CreateSavepointAsync("checkpoint", Token);
        if (!setupFirst)
            await database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", Token);

        Assert.Equal(1, await database.DraftOrders.IgnoreQueryFilters().CountAsync(Token));
        await transaction.RollbackToSavepointAsync("checkpoint", Token);
        Assert.Equal(
            setupFirst ? 1 : 0,
            await database.DraftOrders.IgnoreQueryFilters().CountAsync(Token)
        );
        await database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", Token);
        Assert.Equal(1, await database.DraftOrders.IgnoreQueryFilters().CountAsync(Token));
        Assert.Equal("wholesale-alpha", tenancy.Current.RequireTenant().Value);
    }

    [Fact]
    public async Task NativeTransientRetryEstablishesContextInsideEachFreshTransactionAttempt()
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider(retry: true);
        await using var strategyScope = TenantIsolationJourney.CreateScope(
            provider,
            "wholesale-alpha",
            "application-alpha"
        );
        var operationTenant = strategyScope
            .ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .Current;
        var operationActor = strategyScope
            .ServiceProvider.GetRequiredService<IActorContextAccessor>()
            .Current;
        var strategy = strategyScope
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .Database.CreateExecutionStrategy();
        int attempts = 0;
        var initialStates = new List<bool>();
        await strategy.ExecuteAsync(async () =>
        {
            attempts++;
            await using var attempt = provider.CreateAsyncScope();
            attempt
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(operationTenant);
            attempt
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(operationActor);
            var database = attempt.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var connection = (NpgsqlConnection)database.Database.GetDbConnection();
            var native = (NpgsqlTransaction)transaction.GetDbTransaction();
            initialStates.Add(
                await ValueAsync<bool>(
                    connection,
                    native,
                    "SELECT NULLIF(current_setting('app.tenant_key', true), '') IS NULL"
                )
            );
            await database.EstablishTenantDatabaseContextAsync(
                attempt.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                "app.tenant_key",
                Token
            );
            if (attempts == 1)
                await CommandAsync(
                    connection,
                    native,
                    "DO $$ BEGIN RAISE EXCEPTION USING ERRCODE='40001', MESSAGE='controlled transient proof'; END $$"
                );

            Assert.IsType<DraftOrderChangeResult.Changed>(
                await attempt
                    .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                    .ChangeLinesAsync(
                        new(
                            DraftOrderAccessDemoSeed.OrderId,
                            1,
                            [
                                new DraftOrderLineChange.Change(
                                    DraftOrderAccessDemoSeed.LineId,
                                    2,
                                    25
                                ),
                            ]
                        ),
                        Token
                    )
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        });
        Assert.Equal(2, attempts);
        Assert.Equal(2, initialStates.Count);
        Assert.All(initialStates, Assert.True);
        await using var inspect = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(0, await CountAsync(inspect));
        await using var verify = await inspect.BeginTransactionAsync(Token);
        await SelectTenantAsync(inspect, verify, "wholesale-alpha");
        Assert.Equal(
            2,
            await ValueAsync<int>(inspect, verify, "SELECT version FROM sales.draft_orders")
        );
    }

    [Fact]
    public async Task ReconnectingPhysicalSessionRequiresFreshTenantSetup()
    {
        await using var proof = await CreateAsync();
        int first;
        await using (var connection = await proof.Runtime.OpenConnectionAsync(Token))
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            first = await ValueAsync<int>(connection, transaction, "SELECT pg_backend_pid()");
            await SelectTenantAsync(connection, transaction, "wholesale-alpha");
            Assert.Equal(1, await CountAsync(connection, transaction));
            await transaction.CommitAsync(Token);
        }

        await proof.AdminAsync($"SELECT pg_terminate_backend({first})");
        proof.Runtime.Clear();
        await using var reconnected = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.NotEqual(first, await ValueAsync<int>(reconnected, null, "SELECT pg_backend_pid()"));
        Assert.Equal(0, await CountAsync(reconnected));
        await using var next = await reconnected.BeginTransactionAsync(Token);
        await SelectTenantAsync(reconnected, next, "wholesale-beta");
        Assert.Equal(
            "wholesale-beta",
            await ValueAsync<string>(
                reconnected,
                next,
                "SELECT organization_key FROM sales.draft_orders"
            )
        );
    }

    [Fact]
    public async Task ConcurrentOperationsUseDistinctContextsAndPhysicalConnections()
    {
        await using var proof = await CreateAsync(poolSize: 2);
        await using var provider = proof.Provider();
        var alphaReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var betaReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        async Task<int> ReadAsync(string tenant, TaskCompletionSource ready, Task other)
        {
            await using var operation = TenantIsolationJourney.CreateScope(
                provider,
                tenant,
                "application-alpha"
            );
            var database = operation.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await database.EstablishTenantDatabaseContextAsync(
                operation.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                "app.tenant_key",
                Token
            );
            ready.SetResult();
            await other.WaitAsync(TimeSpan.FromSeconds(10), Token);
            var connection = (NpgsqlConnection)database.Database.GetDbConnection();
            var native = (NpgsqlTransaction)transaction.GetDbTransaction();
            Assert.Equal(
                tenant,
                await ValueAsync<string>(
                    connection,
                    native,
                    "SELECT organization_key FROM sales.draft_orders"
                )
            );
            return await ValueAsync<int>(connection, native, "SELECT pg_backend_pid()");
        }

        int[] backends = await Task.WhenAll(
            ReadAsync("wholesale-alpha", alphaReady, betaReady.Task),
            ReadAsync("wholesale-beta", betaReady, alphaReady.Task)
        );
        Assert.NotEqual(backends[0], backends[1]);
    }

    [Fact]
    public async Task NoResetControlLeaksSessionSettingAndTheSupportedCompositionRejectsIt()
    {
        await using var proof = await CreateAsync();
        var settings = new NpgsqlConnectionStringBuilder(proof.RuntimeConnection)
        {
            NoResetOnClose = true,
        };
        await using var unsafePool = NpgsqlDataSource.Create(settings.ConnectionString);
        Assert.Throws<ArgumentException>(() =>
            TenantIsolationJourney.CreateRuntimeProvider(unsafePool)
        );
        int first;
        await using (var connection = await unsafePool.OpenConnectionAsync(Token))
        {
            first = await ValueAsync<int>(connection, null, "SELECT pg_backend_pid()");
            await CommandAsync(connection, null, "SET app.tenant_key='wholesale-alpha'");
        }

        await using var reused = await unsafePool.OpenConnectionAsync(Token);
        Assert.Equal(first, await ValueAsync<int>(reused, null, "SELECT pg_backend_pid()"));
        Assert.Equal(1, await CountAsync(reused));
    }

    [Fact]
    public async Task LocalSettingRestoresOldSessionValueInsteadOfErasingItsAuthority()
    {
        await using var proof = await CreateAsync();
        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        await CommandAsync(connection, null, "SET app.tenant_key='wholesale-alpha'");
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await SelectTenantAsync(connection, transaction, "wholesale-beta");
            Assert.Equal(
                "wholesale-beta",
                await ValueAsync<string>(
                    connection,
                    transaction,
                    "SELECT organization_key FROM sales.draft_orders"
                )
            );
            await transaction.CommitAsync(Token);
        }

        Assert.Equal(
            "wholesale-alpha",
            await ValueAsync<string>(
                connection,
                null,
                "SELECT organization_key FROM sales.draft_orders"
            )
        );
    }

    [Fact]
    public async Task NativeCopyExportHonorsRowsAndCopyImportIsUnsupportedUnderEffectiveRls()
    {
        await using var proof = await CreateAsync();
        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await SelectTenantAsync(connection, transaction, "wholesale-alpha");
        using (
            var export = await connection.BeginTextExportAsync(
                "COPY (SELECT organization_key FROM sales.draft_orders) TO STDOUT",
                Token
            )
        )
            Assert.Equal("wholesale-alpha\n", await export.ReadToEndAsync(Token));

        await DeniedAsync(
            async () =>
            {
                await using var import = await connection.BeginBinaryImportAsync(
                    "COPY sales.customer_profiles (id, organization_key, code, display_name, version) FROM STDIN (FORMAT BINARY)",
                    Token
                );
            },
            PostgresErrorCodes.FeatureNotSupported
        );
    }
}
