namespace Gloaming.Samples.Wholesale.Api.HttpIntegration;

// Sample policy: public Organization lookup, separately from native authentication metadata.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PublicOrganizationAccessAttribute : Attribute;
