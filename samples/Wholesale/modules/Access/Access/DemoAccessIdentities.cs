using Gloaming.Samples.Wholesale.Access.Contracts;

namespace Gloaming.Samples.Wholesale.Access;

// Explicit demo setup inputs, not runtime account provisioning policy.
public sealed record DemoAccessIdentities(ExternalIdentity Alpha, ExternalIdentity Beta);
