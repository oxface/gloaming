using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var password = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", password: password).WithImageTag("18.6");
if (builder.Configuration.GetValue("LocalDevelopment:UseDataVolume", true))
    postgres.WithDataVolume();
var database = postgres.AddDatabase("wholesale", "wholesale");
bool useRowSecurity = builder.Configuration.GetValue("LocalDevelopment:UseSalesRowSecurity", false);
IResourceBuilder<IResourceWithConnectionString> access = database;
IResourceBuilder<IResourceWithConnectionString>? sales = null;
IResourceBuilder<ParameterResource>? accessPassword = null;
IResourceBuilder<ParameterResource>? salesPassword = null;
if (useRowSecurity)
{
    accessPassword = builder.AddParameter("access-runtime-password", secret: true);
    salesPassword = builder.AddParameter("sales-runtime-password", secret: true);
    var endpoint = postgres.Resource.PrimaryEndpoint;
    access = builder.AddConnectionString(
        "access-runtime",
        ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database=wholesale;Username=wholesale_access"
        )
    );
    sales = builder.AddConnectionString(
        "sales-runtime",
        ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database=wholesale;Username=wholesale_sales"
        )
    );
}

var api = builder
    .AddProject("api", "../HttpIdentityDemo/HttpIdentityDemo.csproj", launchProfileName: null)
    .WithReference(access, connectionName: "Access")
    .WithHttpEndpoint(name: "http")
    .WithHttpsEndpoint(name: "https")
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(database);

// Optional finite command, not a startup dependency or seed reconciliation worker.
var setup = builder
    .AddProject(
        "demo-setup",
        "../HttpIdentityDemo/HttpIdentityDemo.csproj",
        launchProfileName: null
    )
    .WithReference(access, connectionName: "Access")
    .WithArgs(useRowSecurity ? "--initialize-rls-demo" : "--initialize-demo")
    .WaitFor(database)
    .WithExplicitStart();

if (useRowSecurity)
{
    // Passwords remain separate secret parameters. The consumer uses Npgsql's builder to escape
    // them; no admin connection or provisioning password reaches the API.
    api.WithReference(sales!, connectionName: "Sales")
        .WithEnvironment("RuntimePasswords__Access", accessPassword!)
        .WithEnvironment("RuntimePasswords__Sales", salesPassword!)
        .WithEnvironment("ConnectionStrings__Setup", "")
        .WithEnvironment("ConnectionStrings__SalesMigrations", "")
        .WithEnvironment("Parameters__postgres-password", "")
        .WithExplicitStart();
    setup
        .WithReference(database, connectionName: "Setup")
        .WithReference(sales!, connectionName: "Sales")
        .WithEnvironment("RuntimePasswords__Access", accessPassword!)
        .WithEnvironment("RuntimePasswords__Sales", salesPassword!);
}

if (builder.Configuration.GetValue("LocalDevelopment:UseLocalIdentityProvider", false))
{
    var adminPassword = builder.AddParameter("keycloak-password", secret: true);
    var clientSecret = builder.AddParameter("oidc-client-secret", secret: true);
    var userPassword = builder.AddParameter("demo-user-password", secret: true);
    var keycloak = builder
        .AddKeycloak(
            "keycloak",
            builder.Configuration.GetValue("LocalDevelopment:KeycloakPort", 58081),
            adminPassword: adminPassword
        )
        .WithImageTag("26.8.0")
        .WithRealmImport("./Keycloak")
        .WithEnvironment("WHOLESALE_CLIENT_SECRET", clientSecret)
        .WithEnvironment("WHOLESALE_USER_PASSWORD", userPassword)
        .WithEnvironment(
            "WHOLESALE_CALLBACK",
            ReferenceExpression.Create(
                $"{api.GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork)}/signin-oidc"
            )
        );
    var issuer = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/wholesale");
    api.WithReference(keycloak)
        .WithEnvironment("Oidc__Authority", issuer)
        .WithEnvironment("Oidc__ClientId", "wholesale-bff")
        .WithEnvironment("Oidc__ClientSecret", clientSecret)
        .WaitFor(keycloak);
    setup
        .WithEnvironment("DemoIdentity__Issuer", issuer)
        .WithEnvironment("DemoIdentity__AlphaSubject", "20000000-0000-0000-0000-000000000001")
        .WithEnvironment("DemoIdentity__BetaSubject", "20000000-0000-0000-0000-000000000002")
        .WaitFor(keycloak);
}
else
{
    api.WithEnvironment("Oidc__Authority", builder.AddParameter("oidc-authority"))
        .WithEnvironment("Oidc__ClientId", builder.AddParameter("oidc-client-id"));
}

await builder.Build().RunAsync();
