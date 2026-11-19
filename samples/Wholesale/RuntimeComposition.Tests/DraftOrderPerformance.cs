using System.Runtime.InteropServices;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Api;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

public sealed partial class BrowserTests
{
    private const string LoadResource = "draft-order-load";
    private const int OrdersPerTenant = 512;
    private const int PageSize = 20;
    private const int DeepOffset = 180;
    private const string Search =
        "/organizations/south-supply/draft-orders?currency=GBP&minimumTotal=10000";

    [Fact(Explicit = true, Timeout = 300_000)]
    public async Task MeasuresAuthenticatedDraftOrderPages()
    {
        var cancellation = TestContext.Current.CancellationToken;
        string reports = Path.GetFullPath(
            Environment.GetEnvironmentVariable("GLOAMING_PERFORMANCE_RESULTS")
                ?? Directory.CreateTempSubdirectory("wholesale-draft-orders-").FullName
        );
        Directory.CreateDirectory(reports);
        var session = Directory.CreateTempSubdirectory("gloaming-wholesale-session-");
        try
        {
            await using var builder = await CreateBuilderAsync(
                cancellation,
                true,
                $"Performance:ReportsDirectory={reports}",
                $"Performance:SessionDirectory={session.FullName}"
            );
            await using var app = await StartAndInitializeAsync(builder, LoadResource, reports);
            await using var evidence = new RuntimeEvidence(
                app,
                LoadResource,
                reports,
                LoadResource
            );
            await SeedDraftOrdersAsync(app, cancellation);

            var endpoint = app.GetEndpoint("api", "https");
            await LoginAsync(Page, endpoint, "beta");
            int visibleOrders = OrdersPerTenant / 2;
            foreach (int offset in new[] { 0, DeepOffset })
            {
                var page = await GetAsync<DraftOrderPage>(
                    Page,
                    $"{Search}&limit={PageSize}&offset={offset}"
                );
                Assert.Equal(visibleOrders, page.TotalCount);
                Assert.Equal(PageSize, page.Items.Count);
            }

            var cookies = await Context.CookiesAsync([endpoint.ToString()]);
            var identity = Assert.Single(cookies, cookie => cookie.Name == "wholesale.identity");
            await File.WriteAllTextAsync(
                Path.Combine(session.FullName, "request.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        Cookie = $"{identity.Name}={identity.Value}",
                        Search,
                        PageSize,
                        DeepOffset,
                        VisibleOrders = visibleOrders,
                    },
                    JsonSerializerOptions.Web
                ),
                cancellation
            );
            await Page.CloseAsync().WaitAsync(cancellation);
            await File.WriteAllTextAsync(
                Path.Combine(reports, "workload.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        OrdersPerTenant,
                        VisibleOrders = visibleOrders,
                        PageSize,
                        DeepOffset,
                        Application = typeof(DemoSetup).Assembly.GetName().Name,
                        Runtime = RuntimeInformation.FrameworkDescription,
                        OperatingSystem = RuntimeInformation.OSDescription,
                        Environment.ProcessorCount,
                    },
                    JsonSerializerOptions.Web
                ),
                cancellation
            );
            TestContext.Current.TestOutputHelper?.WriteLine($"k6 reports: {reports}");
            if (
                bool.TryParse(builder.Configuration["Performance:LiveDashboard"], out bool live)
                && live
            )
            {
                Uri dashboard = app.GetEndpoint(LoadResource, "dashboard");
                await File.WriteAllTextAsync(
                    Path.Combine(reports, "dashboard.txt"),
                    dashboard.ToString(),
                    cancellation
                );
                TestContext.Current.TestOutputHelper?.WriteLine($"k6 live dashboard: {dashboard}");
            }

            var commands = app.Services.GetRequiredService<ResourceCommandService>();
            var result = await commands.ExecuteCommandAsync(
                LoadResource,
                KnownResourceCommands.StartCommand,
                cancellation
            );
            Assert.True(result.Success, result.Message);
            var terminal = await app.ResourceNotifications.WaitForResourceAsync(
                LoadResource,
                resource =>
                    resource.Snapshot.ExitCode.HasValue
                    || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
                cancellation
            );
            Assert.Equal(0, terminal.Snapshot.ExitCode);
            Assert.True(new FileInfo(Path.Combine(reports, "measured.html")).Length > 0);
            using var summary = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(reports, "summary.json"), cancellation)
            );
            Assert.True(summary.RootElement.TryGetProperty("metrics", out _));
        }
        finally
        {
            session.Delete(recursive: true);
        }
    }

    private static async Task SeedDraftOrdersAsync(
        DistributedApplication app,
        CancellationToken cancellation
    )
    {
        await using var database = new NpgsqlConnection(
            await app.GetConnectionStringAsync("wholesale", cancellation)
        );
        await database.OpenAsync(cancellation);
        await using var seed = new NpgsqlCommand(
            """
            WITH tenants (organization_key, prefix, customer_id) AS (
                VALUES ('wholesale-alpha', 'north', '10000000-0000-0000-0000-000000000001'::uuid),
                       ('wholesale-beta', 'south', '10000000-0000-0000-0000-000000000002'::uuid)
            ), orders AS (
                INSERT INTO sales.draft_orders
                    (id, organization_key, customer_id, code, version, currency, total_limit, is_submitted)
                SELECT gen_random_uuid(), organization_key, customer_id,
                       prefix || '-load-' || lpad(number::text, 4, '0'), 1, 'GBP', 20000, false
                FROM tenants CROSS JOIN generate_series(1, @orders) AS seed(number)
                RETURNING id, organization_key, right(code, 4)::int AS number
            ), lines AS (
                INSERT INTO sales.draft_order_lines (id, order_id, organization_key, sku, quantity, unit_price)
                SELECT gen_random_uuid(), id, organization_key, 'SKU-1', 1, 10000 + number FROM orders
            )
            INSERT INTO sales.draft_order_grants (organization_key, order_id, subject_kind, subject_key, permission)
            SELECT organization_key, id, @subject, 'application-beta', @permission
            FROM orders WHERE number % 2 = 0;
            ANALYZE sales.draft_orders;
            ANALYZE sales.draft_order_lines;
            ANALYZE sales.draft_order_grants;
            """,
            database
        );
        seed.Parameters.AddWithValue("orders", OrdersPerTenant);
        seed.Parameters.AddWithValue("subject", (int)ActorKind.Human);
        seed.Parameters.AddWithValue("permission", (int)DraftOrderAction.Read);
        await seed.ExecuteNonQueryAsync(cancellation);
    }
}
