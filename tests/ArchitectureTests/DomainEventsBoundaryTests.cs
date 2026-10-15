using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using ModulithFoundry.Samples.Wholesale.EventsDemo;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ModulithFoundry.ArchitectureTests;

public sealed class DomainEventsBoundaryTests
{
    [Fact]
    public void ModulesContractsAndLibrariesDoNotDependOnDomainEventsComposition()
    {
        var sources = new[]
        {
            typeof(SalesDbContext).Assembly,
            typeof(InventoryDbContext).Assembly,
            typeof(IDraftOrderCommands).Assembly,
            typeof(Samples.Wholesale.Inventory.Contracts.IStockCatalog).Assembly,
            typeof(Gloaming.Persistence.EntityFrameworkCore.TenantOwnershipExtensions).Assembly,
            typeof(Gloaming.ActorIdentity.Actor).Assembly,
            typeof(Gloaming.Tenancy.TenantContext).Assembly,
        };
        var host = typeof(DomainEventComposition).Assembly;
        var architecture = new ArchLoader().LoadAssemblies([.. sources, host]).Build();
        var forbidden = Types().That().ResideInAssembly(host);
        Assert.NotEmpty(forbidden.GetObjects(architecture));
        foreach (var source in sources)
        {
            var types = Types().That().ResideInAssembly(source);
            Assert.NotEmpty(types.GetObjects(architecture));
            types.Should().NotDependOnAny(forbidden).Check(architecture);
        }
    }
}
