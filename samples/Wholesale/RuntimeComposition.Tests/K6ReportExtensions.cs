using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

internal static class K6ReportExtensions
{
    private const int DashboardPort = 5665;

    public static IResourceBuilder<K6Resource> WithK6Reports(
        this IResourceBuilder<K6Resource> resource,
        string reportsDirectory,
        bool liveDashboard = false
    )
    {
        resource
            .WithBindMount(reportsDirectory, "/reports")
            .WithEnvironment("K6_WEB_DASHBOARD", "true")
            .WithEnvironment(
                "K6_WEB_DASHBOARD_PORT",
                liveDashboard ? DashboardPort.ToString(CultureInfo.InvariantCulture) : "-1"
            )
            .WithEnvironment("K6_WEB_DASHBOARD_PERIOD", "1s")
            .WithEnvironment("K6_WEB_DASHBOARD_EXPORT", "/reports/measured.html");

        if (liveDashboard)
            resource
                .WithEnvironment("K6_WEB_DASHBOARD_HOST", "0.0.0.0")
                .WithHttpEndpoint(name: "dashboard", targetPort: DashboardPort);

        return resource;
    }
}
