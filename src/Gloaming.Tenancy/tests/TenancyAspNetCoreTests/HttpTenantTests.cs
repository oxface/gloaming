using Microsoft.AspNetCore.TestHost;

namespace Gloaming.Tenancy.AspNetCore.Tests;

public sealed class HttpTenantTests
{
    [Fact]
    public async Task TenantlessExceptionOverridesRequiredDefault()
    {
        await using var app = await TestApplication.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(
            "tenantless",
            await client.GetStringAsync("/optional", TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData("/optional/unknown")]
    [InlineData("/optional/%20")]
    [InlineData("/scope/unknown")]
    public async Task FailedCandidateCannotBecomeTenantless(string path)
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(observations.FailurePublished);
        Assert.Empty(observations.EndpointCalls);
    }

    [Theory]
    [InlineData("/optional", 200, "tenantless")]
    [InlineData("/optional/acme", 200, "tenant-alpha")]
    [InlineData("/required/allowed/read", 200, "tenantless")]
    [InlineData("/required/allowed/strict", 400, "")]
    [InlineData("/allowed/required/read", 400, "")]
    [InlineData("/last", 200, "tenantless")]
    [InlineData("/controller/optional", 200, "tenantless")]
    [InlineData("/controller/required", 400, "")]
    public async Task ExplicitMetadataOverridesGroupsAndDefault(
        string path,
        int status,
        string tenant
    )
    {
        await using var app = await TestApplication.StartAsync(TenantRequirement.TenantlessAllowed);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(
            tenant,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task MissingSelectionFailsTheDefaultTenantRequirement()
    {
        await using var app = await TestApplication.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/scope", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RouteCandidatePublishesCanonicalTenantBeforeEndpoint()
    {
        await using var app = await TestApplication.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(
            "tenant-alpha",
            await client.GetStringAsync("/scope/acme", TestContext.Current.CancellationToken)
        );
    }
}
