using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed partial class SalesReplyTests
{
    [Theory(Timeout = 60_000)]
    [InlineData("ConnectionStrings__SalesRepliesSetup")]
    [InlineData("ConnectionStrings__SalesDispatchSetup")]
    [InlineData("ConnectionStrings__SalesDispatch")]
    [InlineData("ConnectionStrings__Sales")]
    [InlineData("ConnectionStrings__Inventory")]
    [InlineData("ConnectionStrings__Access")]
    [InlineData("ConnectionStrings__Setup")]
    [InlineData("ConnectionStrings__SalesMigrations")]
    [InlineData("ConnectionStrings__RabbitMq")]
    [InlineData("Parameters__postgres-password")]
    public async Task ReplyHostRejectsAlternateCredentialsBeforeConnections(string key)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var environment = new Dictionary<string, string>
        {
            ["ConnectionStrings__SalesReplies"] =
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused",
            ["Organization"] = "wholesale-alpha",
            ["Parameters__postgres-password"] = "",
        };
        environment[key] = "deliberately-forbidden-value";
        await using var worker = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            environment,
            "--role",
            "process-sales-replies"
        );
        Assert.NotEqual(0, await worker.WaitForExitAsync(token));
        Assert.Contains(
            "Keep provisioning, alternate database and broker credentials out",
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
    [InlineData("unknown")]
    [InlineData("WHOLESALE-ALPHA")]
    [InlineData(" wholesale-alpha")]
    public async Task OrganizationAdmissionPrecedesDatabaseConnections(string organization)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var worker = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            new()
            {
                ["ConnectionStrings__SalesReplies"] =
                    "Host=127.0.0.1;Port=1;Database=unused;Username=unused",
                ["Organization"] = organization,
                ["Parameters__postgres-password"] = "",
            },
            "--role",
            "process-sales-replies"
        );
        Assert.NotEqual(0, await worker.WaitForExitAsync(token));
        Assert.Contains(
            "Admit wholesale-alpha or wholesale-beta",
            worker.Output,
            StringComparison.Ordinal
        );
    }

    [Theory(Timeout = 60_000)]
    [InlineData("admin-login")]
    [InlineData("bypassrls")]
    [InlineData("membership")]
    [InlineData("revoked-owner")]
    [InlineData("inbox-envelope-update")]
    [InlineData("outbox-payload-read")]
    [InlineData("outbox-delivery-update")]
    [InlineData("other-table-column")]
    [InlineData("disabled-rls")]
    [InlineData("no-force")]
    [InlineData("extra-policy")]
    [InlineData("implicit-with-check")]
    [InlineData("missing-update")]
    [InlineData("tenant-baseline")]
    [InlineData("outbox-policy")]
    public async Task RuntimeRejectsUnsafeRolesPoliciesAndEffectiveColumnGrants(string fault)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        using var identifiers = new NpgsqlCommandBuilder();
        string role = identifiers.QuoteIdentifier(
            new NpgsqlConnectionStringBuilder(proof.Runtime).Username!
        );
        if (fault != "admin-login")
            await proof.SqlAsync(
                fault switch
                {
                    "bypassrls" => $"ALTER ROLE {role} BYPASSRLS",
                    "membership" => $"GRANT pg_read_all_data TO {role}",
                    "revoked-owner" =>
                        $"ALTER TABLE sales.customer_profiles OWNER TO {role}; REVOKE ALL ON sales.customer_profiles FROM {role}",
                    "inbox-envelope-update" =>
                        $"GRANT UPDATE (tenant_key) ON sales.inbox_messages TO {role}",
                    "outbox-payload-read" =>
                        $"GRANT SELECT (payload) ON sales.outbox_messages TO {role}",
                    "outbox-delivery-update" =>
                        $"GRANT UPDATE (attempts) ON sales.outbox_messages TO {role}",
                    "other-table-column" =>
                        "GRANT SELECT (display_name) ON sales.customer_profiles TO PUBLIC",
                    "disabled-rls" =>
                        "ALTER TABLE sales.workflow_instances DISABLE ROW LEVEL SECURITY",
                    "no-force" => "ALTER TABLE sales.inbox_messages NO FORCE ROW LEVEL SECURITY",
                    "extra-policy" =>
                        "CREATE POLICY unsafe_extra ON sales.inbox_messages USING (true)",
                    "implicit-with-check" =>
                        "DROP POLICY gloaming_tenant ON sales.workflow_instances; CREATE POLICY gloaming_tenant ON sales.workflow_instances USING (true)",
                    "missing-update" =>
                        $"REVOKE UPDATE (available_at) ON sales.inbox_messages FROM {role}",
                    "tenant-baseline" =>
                        $"ALTER ROLE {role} SET app.tenant_key = 'wholesale-alpha'",
                    "outbox-policy" =>
                        "DROP POLICY gloaming_outbox_enqueue ON sales.outbox_messages",
                    _ => throw new InvalidOperationException("Unknown startup fault."),
                },
                token
            );

        var environment = proof.Environment();
        if (fault == "admin-login")
            environment["ConnectionStrings__SalesReplies"] = proof.Workflow.SalesConnection;

        await using var worker = proof.StartWorker(environment: environment);
        Assert.NotEqual(0, await worker.WaitForExitAsync(token));
        Assert.Contains(
            "The Sales reply role, policy inventory or grants are unsafe.",
            worker.Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Application started", worker.Output, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task SetupRefusesEmptyStorageWithoutCreatingSchemaOrTheRuntimeLogin()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string setup = await postgres.CreateDatabaseAsync(token);
        var runtime = new NpgsqlConnectionStringBuilder(setup)
        {
            Username = "absent_" + Guid.NewGuid().ToString("N"),
            Password = "not-created",
        };
        await using var process = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            new()
            {
                ["ConnectionStrings__SalesRepliesSetup"] = setup,
                ["ConnectionStrings__SalesReplies"] = runtime.ConnectionString,
            },
            "--role",
            "setup-sales-replies"
        );
        Assert.NotEqual(0, await process.WaitForExitAsync(token));
        Assert.Contains("after explicit module setup", process.Output, StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(setup);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(
            "SELECT NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'sales') AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @runtime)",
            connection
        );
        command.Parameters.AddWithValue("runtime", runtime.Username!);
        Assert.Equal(true, await command.ExecuteScalarAsync(token));
    }
}
