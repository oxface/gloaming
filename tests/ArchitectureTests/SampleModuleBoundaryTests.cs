using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Gloaming.ActorIdentity;
using Gloaming.ActorIdentity.AspNetCore;
using Gloaming.Events.History;
using Gloaming.Events.Serialization;
using Gloaming.EventSourcing;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ReflectionAssembly = System.Reflection.Assembly;

namespace ModulithFoundry.ArchitectureTests;

public sealed class SampleModuleBoundaryTests
{
    private static readonly ReflectionAssembly[] Modules =
    [
        typeof(AccessDbContext).Assembly,
        typeof(InventoryDbContext).Assembly,
        typeof(SalesDbContext).Assembly,
        typeof(PurchasingDbContext).Assembly,
    ];
    private static readonly ReflectionAssembly[] Contracts =
    [
        typeof(IApplicationAccess).Assembly,
        typeof(IStockCatalog).Assembly,
        typeof(ICustomerProfiles).Assembly,
        typeof(IPurchaseOrderHistory).Assembly,
    ];
    private static readonly ReflectionAssembly[] Libraries =
    [
        typeof(ActorId).Assembly,
        typeof(TenantId).Assembly,
        typeof(ActorContextMiddleware).Assembly,
        typeof(TenantContextMiddleware).Assembly,
        typeof(TenantOwnershipExtensions).Assembly,
        typeof(SerializedEvent).Assembly,
        typeof(EventHistory).Assembly,
        typeof(IEventStreamRecord).Assembly,
        typeof(IEventSourcedAggregate<>).Assembly,
        typeof(Gloaming.Messaging.OutgoingMessage).Assembly,
        typeof(Gloaming.Messaging.Workflows.WorkflowStatus).Assembly,
        typeof(Gloaming.Messaging.Workflows.EntityFrameworkCore.WorkflowModelBuilderExtensions).Assembly,
        typeof(Gloaming.Messaging.EntityFrameworkCore.IOutbox<>).Assembly,
        typeof(Gloaming.Messaging.EntityFrameworkCore.Postgres.PostgresOutboxDispatcher<>).Assembly,
    ];
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies([
            .. Modules,
            .. Contracts,
            .. Libraries,
            typeof(Samples.Wholesale.WorkflowDemo.WorkflowComposition).Assembly,
            typeof(Samples.Wholesale.LocalWorkflowDemo.LocalWorkflowComposition).Assembly,
            typeof(Samples.Wholesale.DddDemo.DddComposition).Assembly,
            typeof(DemoComposition).Assembly,
            typeof(DbContext).Assembly,
            typeof(HttpContext).Assembly,
        ])
        .Build();

    [Theory]
    [InlineData("Purchasing", "Access")]
    [InlineData("Access", "Purchasing")]
    [InlineData("Purchasing", "Inventory")]
    [InlineData("Inventory", "Purchasing")]
    [InlineData("Purchasing", "Sales")]
    [InlineData("Sales", "Purchasing")]
    [InlineData("Purchasing", "HttpIdentityDemo")]
    [InlineData("Access", "Sales")]
    [InlineData("Sales", "Access")]
    [InlineData("Inventory", "Sales")]
    [InlineData("Sales", "Inventory")]
    [InlineData("Sales", "HttpIdentityDemo")]
    [InlineData("Access", "Inventory")]
    [InlineData("Inventory", "Access")]
    [InlineData("Access", "HttpIdentityDemo")]
    [InlineData("Inventory", "HttpIdentityDemo")]
    public void ModuleImplementationsDoNotDependOnPeersOrTheHost(string module, string forbidden) =>
        NoDependency(ReflectionAssembly.Load(module), ReflectionAssembly.Load(forbidden));

    [Theory]
    [InlineData("Purchasing.Contracts")]
    [InlineData("Access.Contracts")]
    [InlineData("Inventory.Contracts")]
    [InlineData("Sales.Contracts")]
    public void ContractsDoNotExposePersistenceHttpOrExecutionContexts(string contract)
    {
        foreach (
            ReflectionAssembly forbidden in new[]
            {
                typeof(DbContext).Assembly,
                typeof(HttpContext).Assembly,
            }
                .Concat(Libraries)
                .Concat(Modules)
        )
            NoDependency(ReflectionAssembly.Load(contract), forbidden);
    }

    [Fact]
    public void TechnicalLibrariesDoNotDependOnSampleModuleTypes()
    {
        foreach (ReflectionAssembly library in Libraries)
        foreach (ReflectionAssembly forbidden in Modules.Concat(Contracts))
            NoDependency(library, forbidden);
    }

    [Fact]
    public void SampleCompositionsAreNotDependenciesOfModulesContractsOrLibraries()
    {
        ReflectionAssembly[] hosts =
        [
            typeof(Samples.Wholesale.WorkflowDemo.WorkflowComposition).Assembly,
            typeof(Samples.Wholesale.LocalWorkflowDemo.LocalWorkflowComposition).Assembly,
            typeof(Samples.Wholesale.DddDemo.DddComposition).Assembly,
        ];
        foreach (var host in hosts)
        foreach (var source in Modules.Concat(Contracts).Concat(Libraries))
            NoDependency(source, host);
    }

    [Fact]
    public void BusinessEndpointsCallContractsWithoutModuleImplementationDependencies()
    {
        var endpoints = Types()
            .That()
            .ResideInNamespace("ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints");
        Assert.NotEmpty(endpoints.GetObjects(Architecture));
        foreach (ReflectionAssembly forbidden in Modules)
            endpoints
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(forbidden))
                .Check(Architecture);
    }

    [Fact]
    public void LocalReservationRelaysUseContractsWithoutModulePersistenceOrBrokerDependencies()
    {
        var relays = Types()
            .That()
            .ResideInNamespace("ModulithFoundry.Samples.Wholesale.LocalWorkflowDemo.Delivery");
        Assert.NotEmpty(relays.GetObjects(Architecture));
        foreach (ReflectionAssembly forbidden in Modules.Append(typeof(DbContext).Assembly))
            relays
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(forbidden))
                .Check(Architecture);

        Assert.DoesNotContain(
            typeof(Samples.Wholesale.LocalWorkflowDemo.LocalWorkflowComposition).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.StartsWith("RabbitMQ", StringComparison.Ordinal)
        );
    }

    private static void NoDependency(ReflectionAssembly source, ReflectionAssembly forbidden)
    {
        var types = Types().That().ResideInAssembly(source);
        var forbiddenTypes = Types().That().ResideInAssembly(forbidden);
        Assert.NotEmpty(types.GetObjects(Architecture));
        Assert.NotEmpty(forbiddenTypes.GetObjects(Architecture));
        types.Should().NotDependOnAny(forbiddenTypes).Check(Architecture);
    }
}
