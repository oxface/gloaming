using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

namespace ModulithFoundry.Samples.Wholesale.Sales;

// A finite consumer composition. EF caches this model separately from ordinary Sales options;
// this singleton captures no tenant or scoped service. Existing mappings/filters stay intact.
internal sealed class SalesRowSecurityModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        const string setting = "app.tenant_key";
        modelBuilder.Entity<CustomerRow>().WithRls(setting);
        modelBuilder.Entity<AddressRow>().WithRls(setting);
        modelBuilder.Entity<DraftOrderRow>().WithRls(setting);
        modelBuilder.Entity<DraftOrderLineRow>().WithRls(setting);
        modelBuilder.Entity<DraftOrderGrantRow>().WithRls(setting);
        modelBuilder.Entity<AuditRecord>().WithRls(setting);
    }
}
