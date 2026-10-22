namespace ModulithFoundry.Samples.Wholesale.Api.HttpIntegration;

public static class OrganizationAccessExtensions
{
    public static TBuilder AllowPublicOrganizationAccess<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new PublicOrganizationAccessAttribute());
        return builder;
    }
}
