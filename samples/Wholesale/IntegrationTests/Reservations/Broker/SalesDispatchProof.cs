using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

internal sealed class SalesDispatchProof(WorkflowProof workflow, string runtime) : IAsyncDisposable
{
    internal WorkflowProof Workflow => workflow;
    internal string Runtime => runtime;
    internal Dictionary<string, string> Environment =>
        new()
        {
            ["ConnectionStrings__SalesDispatch"] = runtime,
            ["ConnectionStrings__RabbitMq"] = workflow.Environment["ConnectionStrings__RabbitMq"],
            ["QueuePrefix"] = workflow.Environment["QueuePrefix"],
            ["Parameters__postgres-password"] = "",
        };

    internal static async Task<SalesDispatchProof> CreateAsync(
        PostgreSqlFixture postgres,
        RabbitMqFixture rabbit,
        CancellationToken token
    )
    {
        var workflow = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        string runtime = new Npgsql.NpgsqlConnectionStringBuilder(workflow.SalesConnection)
        {
            Username = "dispatch_" + Guid.NewGuid().ToString("N"),
            Password = "dispatch;\"quotes'and\\slashes",
        }.ConnectionString;
        var proof = new SalesDispatchProof(workflow, runtime);
        try
        {
            // Install the existing six-table business policy without granting dispatch any access.
            var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            foreach (
                string table in new[]
                {
                    "customer_profiles",
                    "customer_addresses",
                    "draft_orders",
                    "draft_order_lines",
                    "draft_order_grants",
                    "audit_entries",
                }
            )
                migration.EnableTenantRowSecurity(
                    table,
                    table == "audit_entries" ? "tenant_key" : "organization_key",
                    "app.tenant_key",
                    "sales"
                );

            foreach (var operation in migration.Operations.Cast<SqlOperation>())
                await WorkflowProof.SqlAsync(workflow.SalesConnection, operation.Sql, token);

            await using var setup = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                new()
                {
                    ["ConnectionStrings__SalesDispatchSetup"] = workflow.SalesConnection,
                    ["ConnectionStrings__SalesDispatch"] = runtime,
                },
                "--role",
                "setup-sales-dispatch"
            );
            Assert.True(await setup.WaitForExitAsync(token) == 0, setup.Output);
            Assert.Contains(
                "Sales dispatch role and grants ready.",
                setup.Output,
                StringComparison.Ordinal
            );
            return proof;
        }
        catch
        {
            await proof.DisposeAsync();
            throw;
        }
    }

    internal ChildHost StartWorker(Dictionary<string, string>? environment = null) =>
        new(
            typeof(WorkflowComposition).Assembly,
            environment ?? Environment,
            "--role",
            "dispatch-sales"
        );

    internal ServiceProvider DispatchServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SalesOutboxDbContext>(options => options.UseNpgsql(runtime));
        services.AddSingleton(workflow.Broker);
        services.AddPostgresOutboxDispatcher<SalesOutboxDbContext, SalesCommandPublisher>(
            new(TimeSpan.FromSeconds(10), TimeSpan.Zero)
        );
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    internal static async Task<OutboxDispatchResult> DispatchAsync(
        ServiceProvider services,
        CancellationToken token
    )
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IOutboxDispatcher<SalesOutboxDbContext>>()
            .DispatchNextAsync(token);
    }

    internal Task<long> CountAsync(string sql, CancellationToken token) =>
        WorkflowProof.CountAsync(workflow.SalesConnection, sql, token);

    internal Task SqlAsync(string sql, CancellationToken token) =>
        WorkflowProof.SqlAsync(workflow.SalesConnection, sql, token);

    public ValueTask DisposeAsync() => workflow.DisposeAsync();
}
