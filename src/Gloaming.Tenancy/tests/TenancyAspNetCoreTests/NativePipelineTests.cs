using System.Net;
using Microsoft.AspNetCore.TestHost;

namespace Gloaming.Tenancy.AspNetCore.Tests;

public sealed class NativePipelineTests
{
    [Theory]
    [InlineData("/public", false)]
    [InlineData("/permissive", false)]
    [InlineData("/scope", true)]
    public async Task AnonymousNamedAndFallbackPoliciesCannotBypassTenantRequirement(
        string path,
        bool authenticated
    )
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(
            observations: observations,
            fallbackPolicy: true
        );
        using var client = app.GetTestClient();
        using var request = authenticated
            ? TestApplication.Request(app, path, ("primary", "user-a"))
            : new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(observations.FailurePublished);
        Assert.Empty(observations.EndpointCalls);
        Assert.Equal(1, observations.MappingCalls[path]);
    }

    [Theory]
    [InlineData(false, "/Account/Login")]
    [InlineData(true, "/Account/AccessDenied")]
    public async Task NativeChallengeAndForbidRunBeforeTenantResolution(
        bool authenticated,
        string destination
    )
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        using var request = authenticated
            ? TestApplication.Request(app, "/permission/acme", ("primary", "user-a"))
            : new HttpRequestMessage(HttpMethod.Get, "/permission/acme");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(destination, response.Headers.Location!.AbsolutePath);
        Assert.Empty(observations.MappingCalls);
        Assert.Empty(observations.EndpointCalls);
    }

    [Fact]
    public async Task CustomUserSelectionReceivesPolicySelectedPrincipalWithoutActorLibrary()
    {
        var observations = new RequestObservations { FromUser = true };
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(
            app,
            "/selected/acme",
            ("primary", "user-a"),
            ("selected", "user-b")
        );
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "tenant-beta",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(["user-b"], observations.Principals);
    }
}
