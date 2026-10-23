using Gloaming.Samples.Wholesale.Api;
using Gloaming.Samples.Wholesale.Api.HttpIntegration;
using Gloaming.Samples.Wholesale.ServiceDefaults;

bool initializeAccess = args is ["--initialize-access"];
bool initializeDemo = args is ["--initialize-demo"];
bool initializeSalesRls = args is ["--initialize-sales-rls"];
bool initializeRlsDemo = args is ["--initialize-rls-demo"];
var builder = WebApplication.CreateBuilder(
    initializeAccess || initializeDemo || initializeSalesRls || initializeRlsDemo ? [] : args
);
DemoDatabasePasswords.Apply(builder.Configuration);
if (initializeRlsDemo)
{
    await RlsDemoSetup.InitializeAsync(builder.Configuration, CancellationToken.None);
    Console.WriteLine("Wholesale RLS demo initialized; HTTP host was not started.");
    return;
}

if (initializeSalesRls)
{
    string owner =
        builder.Configuration.GetConnectionString("SalesMigrations")
        ?? throw new InvalidOperationException(
            "Configure ConnectionStrings:SalesMigrations for finite Sales setup."
        );
    string runtime =
        builder.Configuration.GetConnectionString("Sales")
        ?? throw new InvalidOperationException(
            "Configure ConnectionStrings:Sales for Sales HTTP RLS."
        );
    string access =
        builder.Configuration.GetConnectionString("Access")
        ?? throw new InvalidOperationException("Configure the separate Access runtime connection.");
    await SalesHttpRowSecurity.InstallAsync(owner, runtime, CancellationToken.None);
    await SalesHttpRowSecurity.VerifyRuntimeAsync(runtime, access, CancellationToken.None);
    Console.WriteLine("Sales HTTP row security installed; HTTP host was not started.");
    return;
}

if (initializeAccess || initializeDemo)
{
    string connection =
        builder.Configuration.GetConnectionString("Access")
        ?? throw new InvalidOperationException(
            "Configure ConnectionStrings:Access for the HTTP sample."
        );
    var identities = DemoIdentitySetup.Read(builder.Configuration);
    await DemoSetup.InitializeAsync(connection, initializeDemo, CancellationToken.None, identities);
    Console.WriteLine(
        initializeDemo
            ? "Wholesale demo initialized; HTTP host was not started."
            : "Access demo initialized; HTTP host was not started."
    );
    return;
}

if (
    builder.Configuration.GetConnectionString("Sales") is not null
    && (
        !string.IsNullOrEmpty(builder.Configuration.GetConnectionString("Setup"))
        || !string.IsNullOrEmpty(builder.Configuration.GetConnectionString("SalesMigrations"))
        || !string.IsNullOrEmpty(builder.Configuration["Parameters:postgres-password"])
    )
)
    throw new InvalidOperationException(
        "Keep provisioning credentials out of the Sales RLS HTTP host."
    );

builder.AddServiceDefaults();
DemoComposition.AddServices(builder.Services, builder.Configuration);
builder.Services.AddOrganizationTenancyFromRoute("organization");
var app = builder.Build();
if (builder.Configuration.GetConnectionString("Sales") is { } salesRuntime)
    await SalesHttpRowSecurity.VerifyRuntimeAsync(
        salesRuntime,
        builder.Configuration.GetConnectionString("Access")!,
        CancellationToken.None
    );

DemoComposition.ConfigureHttp(app);
RuntimeHealthEndpoints.Map(app);
DemoComposition.MapOrganizationEndpoints(
    app,
    "/organizations/{organization}/catalog",
    "/organizations/{organization}/identity",
    "/organizations/{organization}/stock/{sku}"
);
Gloaming.Samples.Wholesale.Api.Endpoints.CustomerProfileEndpoints.Map(
    app,
    "/organizations/{organization}/customers/{customerId:guid}/profile"
);
Gloaming.Samples.Wholesale.Api.Endpoints.MembershipEndpoints.Map(
    app,
    "/organizations/{organization}/memberships/{membershipId:guid}"
);
Gloaming.Samples.Wholesale.Api.Endpoints.DraftOrderEndpoints.Map(
    app,
    "/organizations/{organization}/draft-orders",
    allowLifecycle: builder.Configuration.GetConnectionString("Sales") is null
);
await app.RunAsync();
