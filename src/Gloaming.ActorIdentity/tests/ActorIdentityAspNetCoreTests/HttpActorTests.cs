using System.Net;
using Microsoft.AspNetCore.TestHost;

namespace Gloaming.ActorIdentity.AspNetCore.Tests;

public sealed class HttpActorTests
{
    [Fact]
    public async Task NativeCookieMapsToApplicationActorExactlyOnce()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, "/protected", "external-alpha");
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "Human:application-alpha:none",
            await response.Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(1, observations.MappingCalls["/protected"]);
        Assert.Equal(1, observations.NativeAuthentications);
        Assert.Equal(1, observations.NativeAuthorizations);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PolicyFreePublicRequestsRetainAuthenticatedIdentityAndDeliberateAnonymity()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations, fallbackPolicy: false);
        using var client = app.GetTestClient();
        using var identified = TestApplication.Request(app, "/public", "external-alpha");
        using var response = await client.SendAsync(identified, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "Human:application-alpha:none",
            await response.Content.ReadAsStringAsync(Token)
        );
        using var anonymous = await client.GetAsync("/public", Token);
        Assert.Equal("Anonymous:none:none", await anonymous.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, observations.MappingCalls["/public"]);
    }

    [Fact]
    public async Task DefaultAndFallbackChallengesDoNotInvokeBusinessWorkOrTheResolver()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var protectedResponse = await client.GetAsync("/protected", Token);
        using var fallbackResponse = await client.GetAsync("/fallback", Token);
        Assert.Equal(HttpStatusCode.Found, protectedResponse.StatusCode);
        Assert.Equal("/Account/Login", protectedResponse.Headers.Location!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Found, fallbackResponse.StatusCode);
        Assert.Equal("/Account/Login", fallbackResponse.Headers.Location!.AbsolutePath);
        Assert.Empty(observations.MappingCalls);
        Assert.Empty(observations.EndpointCalls);
        using var request = TestApplication.Request(app, "/fallback", "external-beta");
        using var identified = await client.SendAsync(request, Token);
        Assert.Equal(
            "Human:application-beta:none",
            await identified.Content.ReadAsStringAsync(Token)
        );
    }

    [Fact]
    public async Task AnonymousMetadataAndPermissiveNamedPolicyObserveExplicitAnonymity()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var publicResponse = await client.GetAsync("/public", Token);
        using var permissiveResponse = await client.GetAsync("/permissive", Token);
        Assert.Equal("Anonymous:none:none", await publicResponse.Content.ReadAsStringAsync(Token));
        Assert.Equal(
            "Anonymous:none:none",
            await permissiveResponse.Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(
            (null, "Anonymous:none:none"),
            Assert.Single(observations.AuthorizationActors)
        );
        Assert.Empty(observations.MappingCalls);
        using var request = TestApplication.Request(app, "/public", "external-alpha");
        using var identified = await client.SendAsync(request, Token);
        Assert.Equal(
            "Human:application-alpha:none",
            await identified.Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(1, observations.MappingCalls["/public"]);
    }

    [Theory]
    [InlineData("/selected")]
    [InlineData("/selected-controller")]
    public async Task PolicySelectedIdentityIsAvailableToAuthorizationAndTheEndpoint(string path)
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, path, "external-alpha");
        request.Headers.Add("X-Secondary", "present");
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "Human:application-beta:none",
            await response.Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(
            ("external-beta", "Human:application-beta:none"),
            Assert.Single(observations.AuthorizationActors)
        );
        Assert.Equal(1, observations.MappingCalls[path]);
    }

    [Fact]
    public async Task FailedSelectedSchemeDoesNotRetainTheDefaultCookiesActor()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, "/selected", "external-alpha");
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("secondary", Assert.Single(response.Headers.GetValues("X-Challenge-Scheme")));
        Assert.Equal(
            (null, "Anonymous:none:none"),
            Assert.Single(observations.AuthorizationActors)
        );
        Assert.Empty(observations.MappingCalls);
        Assert.Empty(observations.EndpointCalls);
    }

    [Theory]
    [InlineData("/protected", "unknown")]
    [InlineData("/public", "unknown")]
    [InlineData("/public", "anonymous-output")]
    public async Task MappingFailuresStopExecutionAndLetTheConsumerChooseTheResponse(
        string path,
        string subject
    )
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, path, subject);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("consumer mapping rejection", await response.Content.ReadAsStringAsync(Token));
        Assert.Empty(observations.EndpointCalls);
        Assert.Empty(observations.AuthorizationActors);
        Assert.Equal(1, observations.MappingCalls[path]);
    }

    [Fact]
    public async Task MappedActorPresenceDoesNotGrantPermission()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, "/forbidden", "external-alpha");
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location!.AbsolutePath);
        Assert.Equal(
            ("external-alpha", "Human:application-alpha:none"),
            Assert.Single(observations.AuthorizationActors)
        );
        Assert.Empty(observations.EndpointCalls);
    }

    [Fact]
    public async Task OverlappingRequestsAndFollowingAnonymousWorkKeepSeparateContexts()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var alpha = TestApplication.Request(app, "/overlap", "external-alpha");
        using var beta = TestApplication.Request(app, "/overlap", "external-beta");
        var responses = await Task.WhenAll(
                client.SendAsync(alpha, Token),
                client.SendAsync(beta, Token)
            )
            .WaitAsync(TimeSpan.FromSeconds(10), Token);
        using var alphaResponse = responses[0];
        using var betaResponse = responses[1];
        Assert.Equal(
            "Human:application-alpha:none",
            await alphaResponse.Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(
            "Human:application-beta:none",
            await betaResponse.Content.ReadAsStringAsync(Token)
        );
        Assert.Equal(2, observations.MappingCalls["/overlap"]);
        using var anonymous = await client.GetAsync("/public", Token);
        Assert.Equal("Anonymous:none:none", await anonymous.Content.ReadAsStringAsync(Token));
        Assert.False(observations.MappingCalls.ContainsKey("/public"));
    }

    [Fact]
    public async Task CancelingAnAwaitingResolverPublishesNothingAndAFreshRequestSucceeds()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var request = TestApplication.Request(app, "/cancel", "deferred");
        var pending = client.SendAsync(request, cancellation.Token);
        await observations.ResolverEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(
            await observations.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10), Token)
        );
        Assert.False(observations.EndpointCalls.ContainsKey("/cancel"));
        Assert.Equal(1, observations.MappingCalls["/cancel"]);
        using var fresh = TestApplication.Request(app, "/protected", "external-beta");
        using var response = await client.SendAsync(fresh, Token);
        Assert.Equal(
            "Human:application-beta:none",
            await response.Content.ReadAsStringAsync(Token)
        );
    }

    [Fact]
    public async Task AChangedPrincipalCannotReuseAnEstablishedActor()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(
            observations,
            replacePrincipal: true
        );
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, "/protected", "external-alpha");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request, Token));
        Assert.Equal("Human:application-alpha:none", observations.BeforeReplacement);
        Assert.Empty(observations.EndpointCalls);
        Assert.Equal(1, observations.MappingCalls["/protected"]);
    }

    [Fact]
    public async Task ConsumerSuppliedSystemActorAndInitiatorArePreserved()
    {
        var observations = new RequestObservations();
        await using var app = await TestApplication.StartAsync(observations);
        using var client = app.GetTestClient();
        using var request = TestApplication.Request(app, "/protected", "workflow");
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(
            "System:maintenance:initiating-user",
            await response.Content.ReadAsStringAsync(Token)
        );
    }
}
