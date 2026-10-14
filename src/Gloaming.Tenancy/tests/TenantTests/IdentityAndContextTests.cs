using Gloaming.Tenancy;

namespace Gloaming.TenantTests;

public sealed class IdentityAndContextTests
{
    [Fact]
    public void NullKeysAreRejected() =>
        Assert.Throws<ArgumentNullException>(() => new TenantId(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankKeysAreRejected(string key) =>
        Assert.Throws<ArgumentException>(() => new TenantId(key));

    [Theory]
    [InlineData("ACME", "acme")]
    [InlineData("é", "e\u0301")]
    [InlineData(" 42", "42")]
    [InlineData("42 ", "42")]
    [InlineData("00042", "42")]
    public void KeysPreserveExactValuesAndCompareOrdinally(string first, string second)
    {
        var tenant = new TenantId(first);
        Assert.Equal(first, tenant.Value);
        Assert.Equal(tenant, new TenantId(first));
        Assert.NotEqual(tenant, new TenantId(second));
    }

    [Fact]
    public void SelectedTenantRequiresAKeyAndReturnsThatExactIdentity()
    {
        Assert.Throws<ArgumentNullException>(() => TenantContext.ForTenant(null!));
        var tenant = new TenantId("alpha");
        TenantContext context = TenantContext.ForTenant(tenant);
        Assert.Same(tenant, context.Tenant);
        Assert.Same(tenant, context.RequireTenant());
        Assert.Equal(context, TenantContext.ForTenant(new TenantId("alpha")));
        Assert.NotEqual(context, TenantContext.ForTenant(new TenantId("beta")));
        Assert.NotEqual(context, TenantContext.Tenantless());
    }

    [Fact]
    public void DeliberateTenantlessExecutionDoesNotSatisfyTenantRequirement()
    {
        TenantContext context = TenantContext.Tenantless();
        Assert.Null(context.Tenant);
        Assert.Equal(context, TenantContext.Tenantless());
        Assert.Throws<TenantRequiredException>(() => context.RequireTenant());
    }

    [Fact]
    public void RequirementCheckRejectsMissingContext()
    {
        TenantContext absent = null!;
        Assert.Throws<ArgumentNullException>(() => absent.RequireTenant());
    }
}
