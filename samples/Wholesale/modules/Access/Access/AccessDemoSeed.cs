using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;

namespace ModulithFoundry.Samples.Wholesale.Access;

// Only the explicitly invoked setup mode uses these disposable demonstration rows.
public static class AccessDemoSeed
{
    public static void Stage(AccessDbContext database, DemoAccessIdentities? identities = null)
    {
        identities ??= new DemoAccessIdentities(
            new Contracts.ExternalIdentity("https://identity.test", "shared-subject"),
            new Contracts.ExternalIdentity("https://other-identity.test", "shared-subject")
        );
        ArgumentNullException.ThrowIfNull(identities.Alpha);
        ArgumentNullException.ThrowIfNull(identities.Beta);
        database.AddRange(
            new UserRow { Id = "application-alpha" },
            new UserRow { Id = "application-beta" }
        );
        database.AddRange(
            new ExternalIdentityRow
            {
                Issuer = identities.Alpha.Issuer,
                Subject = identities.Alpha.Subject,
                UserId = "application-alpha",
            },
            new ExternalIdentityRow
            {
                Issuer = identities.Beta.Issuer,
                Subject = identities.Beta.Subject,
                UserId = "application-beta",
            }
        );
        database.AddRange(
            new OrganizationRow { Id = "wholesale-alpha", Slug = "north-supply" },
            new OrganizationRow { Id = "wholesale-beta", Slug = "south-supply" }
        );
        database.AddRange(
            Membership("application-alpha", "wholesale-alpha", administrator: true),
            Membership("application-alpha", "wholesale-beta"),
            Membership("application-beta", "wholesale-beta", administrator: true)
        );
    }

    private static MembershipRow Membership(
        string user,
        string organization,
        bool administrator = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = user,
            OrganizationId = organization,
            Status = MembershipStatus.Active,
            CanAdministerMemberships = administrator,
        };
}
