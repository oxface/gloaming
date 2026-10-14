using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;

public static class OrganizationEndpoints
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        string catalogPattern,
        string identityPattern,
        string stockPattern
    )
    {
        endpoints
            .MapGet(catalogPattern, ReadCatalog)
            .AllowAnonymous()
            .AllowPublicOrganizationAccess();
        endpoints.MapGet(stockPattern, ReadCatalog).RequireAuthorization();
        endpoints.MapGet(identityPattern, ReadTenantIdentity).RequireAuthorization();
    }

    private static Ok<TenantIdentityResponse> ReadTenantIdentity(
        IActorContextAccessor actor,
        ITenantContextAccessor tenant
    ) => TypedResults.Ok(TenantIdentity(actor, tenant));

    private static async Task<Results<Ok<CatalogResponse>, ProblemHttpResult>> ReadCatalog(
        string? sku,
        IStockCatalog catalog,
        IActorContextAccessor actor,
        ITenantContextAccessor tenant,
        CancellationToken cancellationToken
    )
    {
        sku ??= "DEMO-NOTEBOOK";
        if (string.IsNullOrWhiteSpace(sku))
            return TypedResults.Problem(
                title: "Select a SKU.",
                statusCode: StatusCodes.Status400BadRequest
            );
        StockAvailability? stock = await catalog.ReadAsync(sku, cancellationToken);
        return stock is null
            ? TypedResults.Problem(
                title: "Stock item not found.",
                statusCode: StatusCodes.Status404NotFound
            )
            : TypedResults.Ok(new CatalogResponse(TenantIdentity(actor, tenant), stock));
    }

    private static TenantIdentityResponse TenantIdentity(
        IActorContextAccessor actor,
        ITenantContextAccessor tenant
    ) =>
        new(
            actor.Current.Actor.Kind,
            actor.Current.Actor.Id?.Value,
            tenant.Current.RequireTenant().Value
        );
}
