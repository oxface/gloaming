using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ModulithFoundry.ArchitectureTests;

public sealed class ProductCompositionBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModulesContractsAndLibrariesDoNotDependOnProductCompositions(bool worker)
    {
        var sources = new[]
        {
            typeof(SalesDbContext).Assembly,
            typeof(Samples.Wholesale.Dispatch.DispatchDbContext).Assembly,
            typeof(Samples.Wholesale.Dispatch.Contracts.ICustomerDirectory).Assembly,
            typeof(InventoryDbContext).Assembly,
            typeof(IDraftOrderCommands).Assembly,
            typeof(Samples.Wholesale.Inventory.Contracts.IStockCatalog).Assembly,
            typeof(Gloaming.Persistence.EntityFrameworkCore.TenantOwnershipExtensions).Assembly,
            typeof(Gloaming.ActorIdentity.Actor).Assembly,
            typeof(Gloaming.Tenancy.TenantContext).Assembly,
        };
        var host = worker
            ? typeof(Samples.Wholesale.Worker.Broker.WorkflowComposition).Assembly
            : typeof(DemoComposition).Assembly;
        var architecture = new ArchLoader().LoadAssemblies([.. sources, host]).Build();
        var forbidden = Types().That().ResideInAssembly(host);
        Assert.NotEmpty(forbidden.GetObjects(architecture));
        foreach (var source in sources)
        {
            var types = Types().That().ResideInAssembly(source);
            Assert.NotEmpty(types.GetObjects(architecture));
            types.Should().NotDependOnAny(forbidden).Check(architecture);
            Assert.DoesNotContain(
                source.GetReferencedAssemblies(),
                dependency => dependency.Name == "Wholesale.IntegrationTests"
            );
        }
    }
}
