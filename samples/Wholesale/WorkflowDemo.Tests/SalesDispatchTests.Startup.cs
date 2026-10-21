using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed partial class SalesDispatchTests
{
    [Theory(Timeout = 60_000)]
    [InlineData("ConnectionStrings__SalesDispatchSetup")]
    [InlineData("ConnectionStrings__SalesReplies")]
    [InlineData("ConnectionStrings__SalesRepliesSetup")]
    [InlineData("ConnectionStrings__Sales")]
    [InlineData("ConnectionStrings__Inventory")]
    [InlineData("ConnectionStrings__Access")]
    [InlineData("ConnectionStrings__Setup")]
    [InlineData("ConnectionStrings__SalesMigrations")]
    [InlineData("Parameters__postgres-password")]
    public async Task DispatchRejectsRetainedBusinessOrProvisioningCredentialsBeforeConnections(
        string key
    )
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var environment = new Dictionary<string, string>
        {
            ["ConnectionStrings__SalesDispatch"] =
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused",
            [key] = "deliberately-forbidden-value",
        };
        await using var worker = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            environment,
            "--role",
            "dispatch-sales"
        );
        Assert.NotEqual(0, await worker.WaitForExitAsync(token));
        Assert.Contains(
            "Keep provisioning and business database credentials out",
            worker.Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "deliberately-forbidden-value",
            worker.Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Application started", worker.Output, StringComparison.Ordinal);
    }

    [Theory(Timeout = 60_000)]
    [InlineData("admin-login")]
    [InlineData("table-update")]
    [InlineData("envelope-column")]
    [InlineData("business-column")]
    [InlineData("public-business-column")]
    [InlineData("membership")]
    [InlineData("bypassrls")]
    [InlineData("owner")]
    [InlineData("business-owner-revoked")]
    [InlineData("schema-owner-revoked")]
    [InlineData("database-owner-revoked")]
    [InlineData("grant-option")]
    [InlineData("missing-column")]
    [InlineData("missing-accounting-column")]
    [InlineData("operator-column")]
    [InlineData("outbox-rls")]
    public async Task StartupRejectsUnsafeEffectiveRolesAndColumnGrants(string fault)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        using var identifiers = new NpgsqlCommandBuilder();
        var runtime = new NpgsqlConnectionStringBuilder(proof.Runtime);
        string role = identifiers.QuoteIdentifier(runtime.Username!);
        string database = identifiers.QuoteIdentifier(runtime.Database!);
        if (fault != "admin-login")
            await proof.SqlAsync(
                fault switch
                {
                    "table-update" => $"GRANT UPDATE ON sales.outbox_messages TO {role}",
                    "envelope-column" =>
                        $"GRANT UPDATE (payload) ON sales.outbox_messages TO {role}",
                    "business-column" =>
                        $"GRANT SELECT (display_name) ON sales.customer_profiles TO {role}",
                    "public-business-column" =>
                        "GRANT SELECT (display_name) ON sales.customer_profiles TO PUBLIC",
                    "membership" => $"GRANT pg_read_all_data TO {role}",
                    "bypassrls" => $"ALTER ROLE {role} BYPASSRLS",
                    "owner" => $"ALTER TABLE sales.outbox_messages OWNER TO {role}",
                    "business-owner-revoked" =>
                        $"ALTER TABLE sales.customer_profiles OWNER TO {role}; REVOKE ALL ON sales.customer_profiles FROM {role}",
                    "schema-owner-revoked" =>
                        $"ALTER SCHEMA sales OWNER TO {role}; REVOKE CREATE ON SCHEMA sales FROM {role}",
                    "database-owner-revoked" =>
                        $"ALTER DATABASE {database} OWNER TO {role}; REVOKE CREATE ON DATABASE {database} FROM {role}",
                    "grant-option" =>
                        $"GRANT UPDATE (attempts) ON sales.outbox_messages TO {role} WITH GRANT OPTION",
                    "missing-column" =>
                        $"REVOKE UPDATE (attempts) ON sales.outbox_messages FROM {role}",
                    "missing-accounting-column" =>
                        $"REVOKE UPDATE (failure_count) ON sales.outbox_messages FROM {role}",
                    "operator-column" =>
                        $"GRANT UPDATE (redrive_count) ON sales.outbox_messages TO {role}",
                    "outbox-rls" => "ALTER TABLE sales.outbox_messages ENABLE ROW LEVEL SECURITY",
                    _ => throw new InvalidOperationException("Unknown privilege fault."),
                },
                token
            );

        var environment = proof.Environment;
        if (fault == "admin-login")
            environment["ConnectionStrings__SalesDispatch"] = proof.Workflow.SalesConnection;

        // A bogus broker endpoint demonstrates that the database guard runs before transport startup.
        environment["ConnectionStrings__RabbitMq"] = "amqp://unused:unused@127.0.0.1:1";
        await using var worker = proof.StartWorker(environment);
        Assert.NotEqual(0, await worker.WaitForExitAsync(token));
        Assert.Contains(
            "The Sales dispatch role or outbox grants are unsafe.",
            worker.Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Application started", worker.Output, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task EmptyStorageIsRejectedWithoutCreatingSchemaOrTheDispatchRole()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string setupConnection = await postgres.CreateDatabaseAsync(token);
        var runtime = new NpgsqlConnectionStringBuilder(setupConnection)
        {
            Username = "absent_" + Guid.NewGuid().ToString("N"),
            Password = "not-created",
        };
        foreach (string operation in new[] { "dispatch-sales", "setup-sales-dispatch" })
        {
            var environment = new Dictionary<string, string>
            {
                ["ConnectionStrings__SalesDispatch"] = runtime.ConnectionString,
                ["Parameters__postgres-password"] = "",
            };
            if (operation == "setup-sales-dispatch")
                environment["ConnectionStrings__SalesDispatchSetup"] = setupConnection;

            await using var process = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                environment,
                "--role",
                operation
            );
            Assert.NotEqual(0, await process.WaitForExitAsync(token));
        }

        await using var connection = new NpgsqlConnection(setupConnection);
        await connection.OpenAsync(token);
        await using var check = new NpgsqlCommand(
            "SELECT NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'sales') AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @runtime)",
            connection
        );
        check.Parameters.AddWithValue("runtime", runtime.Username!);
        Assert.Equal(true, await check.ExecuteScalarAsync(token));
    }

    [Fact(Timeout = 60_000)]
    public async Task WorkerDoesNotRecreateMissingQueuesAndProvisioningDoesNotAlterExistingRoles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesDispatchProof.CreateAsync(postgres, rabbit, token);
        await proof.Workflow.StartAsync(Request(), token);
        await using (
            var setup = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                new()
                {
                    ["ConnectionStrings__SalesDispatchSetup"] = proof.Workflow.SalesConnection,
                    ["ConnectionStrings__SalesDispatch"] = proof.Runtime,
                },
                "--role",
                "setup-sales-dispatch"
            )
        )
        {
            Assert.NotEqual(0, await setup.WaitForExitAsync(token));
            Assert.Contains("already exists", setup.Output, StringComparison.Ordinal);
        }

        await proof.Workflow.Broker.DeleteQueuesAsync(token);
        await using (var worker = proof.StartWorker())
        {
            Assert.NotEqual(0, await worker.WaitForExitAsync(token));
            Assert.Contains("NOT_FOUND", worker.Output, StringComparison.Ordinal);
        }

        await using var connection = await new ConnectionFactory
        {
            Uri = new Uri(rabbit.ConnectionString),
        }.CreateConnectionAsync(token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: token);
        var missing = await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            channel.QueueDeclarePassiveAsync(
                proof.Environment["QueuePrefix"] + ".inventory.issue-stock",
                token
            )
        );
        Assert.NotNull(missing.ShutdownReason);
        Assert.Equal(404, missing.ShutdownReason.ReplyCode);
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE attempts = 0 AND dispatched_at IS NULL",
                token
            )
        );
        // Restore only this proof's disposable topology so the existing fixture can clean it up.
        await proof.Workflow.Broker.SetupAsync(token);
    }
}
