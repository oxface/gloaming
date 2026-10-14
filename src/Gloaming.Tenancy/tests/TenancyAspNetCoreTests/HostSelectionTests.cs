using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Gloaming.Tenancy.AspNetCore.Tests;

public sealed class HostSelectionTests
{
    [Fact]
    public void CandidateTextIsPreservedForConsumerCanonicalization()
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("ACME.TENANTS.EXAMPLE.TEST.:8443");
        context.Request.RouteValues["organization"] = " AcMe ";
        Assert.Equal("ACME", HttpTenantCandidates.FromSubdomain(context, "tenants.example.test"));
        Assert.Equal(" AcMe ", HttpTenantCandidates.FromRoute(context, "organization"));
    }

    [Theory]
    [InlineData("*.example.test")]
    [InlineData("127.0.0.1")]
    public async Task InvalidBaseDomainFailsPresetRegistration(string domain)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            TestApplication.StartAsync(
                observations: new RequestObservations { BaseDomain = domain }
            )
        );
    }

    [Theory]
    [InlineData("acme.tenants.example.test", 200, "tenant-alpha")]
    [InlineData("ACME.TENANTS.EXAMPLE.TEST.:8443", 200, "tenant-alpha")]
    [InlineData("tenants.example.test", 200, "tenantless")]
    [InlineData("unknown.tenants.example.test", 422, "")]
    [InlineData("acme.other.test", 422, "")]
    [InlineData("nested.acme.tenants.example.test", 422, "")]
    [InlineData("127.0.0.1:8443", 422, "")]
    [InlineData("-acme.tenants.example.test", 422, "")]
    public async Task ConfiguredHostSelectionIsBoundedAndIndependentOfRoute(
        string host,
        int status,
        string tenant
    )
    {
        var observations = new RequestObservations { BaseDomain = "tenants.example.test." };
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/optional/beta");
        request.Headers.Host = host;
        request.Headers.Add("X-Forwarded-Host", "beta.tenants.example.test");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(
            tenant,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }
}
