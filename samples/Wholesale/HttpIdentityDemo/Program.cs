using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.ServiceDefaults;

bool initializeAccess = args is ["--initialize-access"];
bool initializeDemo = args is ["--initialize-demo"];
var builder = WebApplication.CreateBuilder(initializeAccess || initializeDemo ? [] : args);
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
