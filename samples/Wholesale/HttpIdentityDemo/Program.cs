using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.ServiceDefaults;

bool initializeAccess = args is ["--initialize-access"];
bool initializeDemo = args is ["--initialize-demo"];
bool initializeSalesRls = args is ["--initialize-sales-rls"];
var builder = WebApplication.CreateBuilder(
    initializeAccess || initializeDemo || initializeSalesRls ? [] : args
);
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
ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints.CustomerProfileEndpoints.Map(
    app,
    "/organizations/{organization}/customers/{customerId:guid}/profile"
);
ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints.MembershipEndpoints.Map(
    app,
    "/organizations/{organization}/memberships/{membershipId:guid}"
);
ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints.DraftOrderEndpoints.Map(
    app,
    "/organizations/{organization}/draft-orders"
);
await app.RunAsync();
