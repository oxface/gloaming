using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Gloaming.ActorIdentity;
using Gloaming.ActorIdentity.AspNetCore;
using Gloaming.Tenancy;
using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
var token = deadline.Token;
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddScoped<RequestProof>();
var observations = new ConcurrentDictionary<string, RequestProof>();
builder
    .Services.AddAuthentication("primary")
    .AddCookie("primary", Cookies)
    .AddCookie("selected", Cookies);
builder.Services.AddAuthorization(options =>
{
    foreach (string scheme in new[] { "primary", "selected" })
        options.AddPolicy(
            scheme,
            policy =>
                policy
                    .AddAuthenticationSchemes(scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", "read")
                    .RequireAssertion(context =>
                    {
                        var http = (HttpContext)context.Resource!;
                        var proof = http.RequestServices.GetRequiredService<RequestProof>();
                        string subject = http.User.FindFirstValue("sub") ?? "anonymous";
                        proof.Trace.Add("authorize:" + subject);
                        Check(
                            !TenantPublished(http),
                            "Tenant admission must follow native authorization."
                        );
                        Check(
                            ActorName(http) == ApplicationActor(subject),
                            "Authorization must see the policy-selected actor."
                        );
                        return true;
                    })
        );
});
builder.Services.AddHttpActorContext<ActorResolver>();
builder.Services.AddRouteTenancy<TenantResolver>("workspace");
await using var app = builder.Build();
app.Use(
    async (http, next) =>
    {
        var proof = http.RequestServices.GetRequiredService<RequestProof>();
        Check(
            observations.TryAdd(http.Request.Headers["X-Proof"].ToString(), proof),
            "Each request needs a unique observation."
        );
        try
        {
            await next(http);
        }
        catch (HttpActorResolutionException)
        {
            Check(
                !ActorPublished(http) && !TenantPublished(http),
                "Failed mapping must publish neither context."
            );
            http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        }
        catch (HttpTenantResolutionException)
        {
            Check(
                ActorPublished(http) && !TenantPublished(http),
                "Admission failure must not publish tenancy."
            );
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
        catch (TenantRequiredException)
        {
            Check(
                ActorPublished(http) && !TenantPublished(http),
                "Required tenancy must reject deliberate tenantless output before publication."
            );
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
);
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpActorContext();
app.UseHttpTenantContext();
app.MapGet("/protected/{workspace?}", Read).RequireAuthorization("primary");
app.MapGet("/selected/{workspace?}", Read).RequireAuthorization("selected");
app.MapGet("/optional/{workspace?}", Read).AllowAnonymous().AllowTenantless();
app.MapGet("/required/{workspace?}", Read).AllowAnonymous().RequireTenant();
var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int overlapping = 0;
app.MapGet(
        "/overlap/{workspace}",
        async (
            HttpContext http,
            IActorContextAccessor actor,
            ITenantContextAccessor tenancy,
            RequestProof proof
        ) =>
        {
            if (Interlocked.Increment(ref overlapping) == 2)
                bothEntered.TrySetResult();

            await bothEntered.Task.WaitAsync(http.RequestAborted);
            return Read(http, actor, tenancy, proof);
        }
    )
    .RequireAuthorization("primary");
await app.StartAsync(token);
var address = app
    .Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!
    .Addresses.Single();
using var client = new HttpClient(
    new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }
)
{
    BaseAddress = new Uri(address),
};

await Send(
    "/selected/beta",
    HttpStatusCode.OK,
    "user-b:partition-b",
    ["actor:bob", "authorize:bob", "tenant:beta", "endpoint"],
    ("primary", "alice", true),
    ("selected", "bob", true)
);
await Send(
    "/protected/acme",
    HttpStatusCode.OK,
    "user-a:partition-a",
    ["actor:alice", "authorize:alice", "tenant:acme", "endpoint"],
    ("primary", "alice", true)
);
await Send("/protected/acme", HttpStatusCode.Unauthorized, "", ["authorize:anonymous"]);
await Send(
    "/protected/acme",
    HttpStatusCode.Forbidden,
    "",
    ["actor:alice", "authorize:alice"],
    ("primary", "alice", false)
);
await Send("/optional", HttpStatusCode.OK, "anonymous:tenantless", ["tenant:absent", "endpoint"]);
await Send("/required", HttpStatusCode.BadRequest, "", ["tenant:absent"]);
await Send(
    "/optional/unknown",
    HttpStatusCode.Forbidden,
    "",
    ["actor:alice", "tenant:unknown"],
    ("primary", "alice", true)
);
await Send(
    "/optional/beta",
    HttpStatusCode.Forbidden,
    "",
    ["actor:alice", "tenant:beta"],
    ("primary", "alice", true)
);
await Send(
    "/protected/acme",
    HttpStatusCode.UnprocessableEntity,
    "",
    ["actor:unmapped"],
    ("primary", "unmapped", true)
);
await Send(
    "/optional/acme",
    HttpStatusCode.OK,
    "user-a:partition-a",
    ["actor:alice", "tenant:acme", "endpoint"],
    ("primary", "alice", true)
);
await Send(
    "/selected/beta",
    HttpStatusCode.Unauthorized,
    "",
    ["authorize:anonymous"],
    ("primary", "alice", true)
);
await Task.WhenAll(
    Send(
        "/overlap/acme",
        HttpStatusCode.OK,
        "user-a:partition-a",
        ["actor:alice", "authorize:alice", "tenant:acme", "endpoint"],
        ("primary", "alice", true)
    ),
    Send(
        "/overlap/beta",
        HttpStatusCode.OK,
        "user-b:partition-b",
        ["actor:bob", "authorize:bob", "tenant:beta", "endpoint"],
        ("primary", "bob", true)
    )
);
await Send("/optional", HttpStatusCode.OK, "anonymous:tenantless", ["tenant:absent", "endpoint"]);
Check(
    observations.Values.Select(proof => proof.Scope).Distinct().Count() == 14,
    "All fourteen requests must have distinct operation scopes."
);
await app.StopAsync(token);
Console.WriteLine(
    "PASS HTTP package adoption: native scheme selection, authorization ordering, once-only mapping, admission failures and request isolation"
);

async Task Send(
    string path,
    HttpStatusCode status,
    string body,
    string[] trace,
    params (string Scheme, string Subject, bool Permission)[] identities
)
{
    string id = Guid.NewGuid().ToString("N");
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Add("X-Proof", id);
    if (identities.Length > 0)
    {
        var cookies = identities.Select(identity =>
        {
            var options = app
                .Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(identity.Scheme);
            var claims = new List<Claim> { new("sub", identity.Subject) };
            if (identity.Permission)
                claims.Add(new Claim("permission", "read"));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, identity.Scheme));
            return options.Cookie.Name
                + "="
                + options.TicketDataFormat.Protect(
                    new AuthenticationTicket(principal, identity.Scheme)
                );
        });
        request.Headers.Add("Cookie", string.Join("; ", cookies));
    }

    using var response = await client.SendAsync(request, token);
    Check(
        response.StatusCode == status,
        $"{path}: expected {status}, received {response.StatusCode}."
    );
    Check(
        await response.Content.ReadAsStringAsync(token) == body,
        $"{path}: wrong established contexts."
    );
    Check(
        observations[id].Trace.SequenceEqual(trace),
        $"{path}: unexpected establishment order: {string.Join(',', observations[id].Trace)}."
    );
}

static string Read(
    HttpContext http,
    IActorContextAccessor actor,
    ITenantContextAccessor tenancy,
    RequestProof proof
)
{
    proof.Trace.Add("endpoint");
    return (actor.Current.Actor.Id?.Value ?? "anonymous")
        + ":"
        + (tenancy.Current.Tenant?.Value ?? "tenantless");
}

static void Cookies(CookieAuthenticationOptions options)
{
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
}

static bool TenantPublished(HttpContext http)
{
    try
    {
        _ = http.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
        return true;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}

static bool ActorPublished(HttpContext http)
{
    try
    {
        _ = http.RequestServices.GetRequiredService<IActorContextAccessor>().Current;
        return true;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}

static string ActorName(HttpContext http) =>
    http.RequestServices.GetRequiredService<IActorContextAccessor>().Current.Actor.Id?.Value
    ?? "anonymous";
static string ApplicationActor(string subject) =>
    subject switch
    {
        "alice" => "user-a",
        "bob" => "user-b",
        _ => "anonymous",
    };
static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class RequestProof
{
    public Guid Scope { get; } = Guid.NewGuid();
    public List<string> Trace { get; } = [];
}

sealed class ActorResolver(RequestProof proof) : IHttpActorContextResolver
{
    public ValueTask<ActorContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? subject = httpContext.User.FindFirstValue("sub");
        proof.Trace.Add("actor:" + subject);
        return ValueTask.FromResult<ActorContext?>(
            subject switch
            {
                "alice" => new ActorContext(Actor.Human(new ActorId("user-a"))),
                "bob" => new ActorContext(Actor.Human(new ActorId("user-b"))),
                _ => null,
            }
        );
    }
}

sealed class TenantResolver(RequestProof proof, IActorContextAccessor actor)
    : IHttpTenantCandidateResolver
{
    public ValueTask<TenantContext?> ResolveAsync(
        string? candidate,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        proof.Trace.Add("tenant:" + (candidate ?? "absent"));
        // This mapping and membership decision belong to this application.
        return ValueTask.FromResult<TenantContext?>(
            (candidate, actor.Current.Actor.Id?.Value) switch
            {
                (null, _) => TenantContext.Tenantless(),
                ("acme", "user-a") => TenantContext.ForTenant(new TenantId("partition-a")),
                ("beta", "user-b") => TenantContext.ForTenant(new TenantId("partition-b")),
                _ => null,
            }
        );
    }
}
