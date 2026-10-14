using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace Gloaming.Tenancy.AspNetCore.Tests;

public sealed class EstablishmentTests
{
    [Fact]
    public async Task UnmatchedEndpointDoesNotAttemptSelection()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(
            "/unmatched",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(observations.MappingCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDoesNotPublishOrInvokeBusinessWork(bool duringResolution)
    {
        using var cancellation = new CancellationTokenSource();
        var observations = new RequestObservations
        {
            BeforeTenant = context =>
            {
                if (context.Request.Path == "/scope/acme")
                {
                    context.RequestAborted = cancellation.Token;
                    if (!duringResolution)
                        cancellation.Cancel();
                }
            },
            Resolve = async (context, token) =>
            {
                if (context.Request.Path != "/scope/acme")
                    return TenantContext.ForTenant(new TenantId("tenant-beta"));
                Assert.Equal(cancellation.Token, token);
                await Task.Yield();
                cancellation.Cancel();
                // A consumer may finish without throwing despite cancellation.
                return TenantContext.ForTenant(new TenantId("tenant-alpha"));
            },
        };
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync("/scope/acme", TestContext.Current.CancellationToken)
        );
        Assert.False(
            await observations.Cancelled.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Empty(observations.EndpointCalls);
        Assert.Equal(duringResolution ? 1 : 0, observations.MappingCalls.Values.Sum());
        Assert.Equal(
            "tenant-beta",
            await client.GetStringAsync("/scope/beta", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task ReplacedPrincipalCannotPublishResolvedTenant()
    {
        var observations = new RequestObservations
        {
            Resolve = (context, _) =>
            {
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim("sub", "changed-user")], "changed")
                );
                return ValueTask.FromResult<TenantContext?>(
                    TenantContext.ForTenant(new TenantId("tenant-alpha"))
                );
            },
        };
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(
            "/scope/acme",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(observations.FailurePublished);
        Assert.Empty(observations.EndpointCalls);
    }

    [Fact]
    public async Task OverlappingScopesAndLaterTenantlessRequestHaveIndependentContexts()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        string[] results = await Task.WhenAll(
            client.GetStringAsync("/overlap/acme", TestContext.Current.CancellationToken),
            client.GetStringAsync("/overlap/beta", TestContext.Current.CancellationToken)
        );
        Assert.Equal(["tenant-alpha", "tenant-beta"], results);
        Assert.Equal(
            "tenantless",
            await client.GetStringAsync("/optional", TestContext.Current.CancellationToken)
        );
        Assert.Equal(3, observations.MappingCalls.Count);
        Assert.All(observations.MappingCalls.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task InvalidRequirementConfigurationFailsStartup()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() =>
            TestApplication.StartAsync((TenantRequirement)0)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerAdmissionAndUnexpectedFaultsPropagateWithoutPublication(
        bool admission
    )
    {
        Exception failure = admission
            ? new UnauthorizedAccessException("Consumer admission denied.")
            : new InvalidOperationException("Consumer lookup failed.");
        bool published = false;
        var observations = new RequestObservations
        {
            Resolve = (context, _) =>
            {
                if (context.Request.Path == "/scope/acme")
                {
                    published = TestApplication.IsPublished(context);
                    throw failure;
                }
                return ValueTask.FromResult<TenantContext?>(
                    TenantContext.ForTenant(new TenantId("tenant-beta"))
                );
            },
        };
        await using var app = await TestApplication.StartAsync(observations: observations);
        using var client = app.GetTestClient();
        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetAsync("/scope/acme", TestContext.Current.CancellationToken)
        );
        Assert.Same(failure, thrown);
        Assert.False(published);
        Assert.Empty(observations.EndpointCalls);
        Assert.Equal(
            "tenant-beta",
            await client.GetStringAsync("/scope/beta", TestContext.Current.CancellationToken)
        );
        Assert.Equal(1, observations.MappingCalls["/scope/acme"]);
    }
}
