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
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
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
        typeof(DispatchDbContext).Assembly,
    ];
    private static readonly ReflectionAssembly[] Contracts =
    [
        typeof(IApplicationAccess).Assembly,
        typeof(IStockCatalog).Assembly,
        typeof(ICustomerProfiles).Assembly,
        typeof(IPurchaseOrderHistory).Assembly,
        typeof(ICustomerDirectory).Assembly,
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
            typeof(Samples.Wholesale.Worker.Broker.WorkflowComposition).Assembly,
            typeof(Samples.Wholesale.Worker.Local.LocalWorkflowComposition).Assembly,
            typeof(DemoComposition).Assembly,
            typeof(DbContext).Assembly,
            typeof(HttpContext).Assembly,
            typeof(Gloaming.Messaging.RabbitMQ.RabbitMqSession).Assembly,
            typeof(RabbitMQ.Client.IConnection).Assembly,
        ])
        .Build();

    [Theory]
    [InlineData("Dispatch", "Access")]
    [InlineData("Dispatch", "Sales")]
    [InlineData("Dispatch", "Inventory")]
    [InlineData("Dispatch", "Purchasing")]
    [InlineData("Dispatch", "Wholesale.Api")]
    [InlineData("Access", "Dispatch")]
    [InlineData("Sales", "Dispatch")]
    [InlineData("Inventory", "Dispatch")]
    [InlineData("Purchasing", "Dispatch")]
    [InlineData("Purchasing", "Access")]
    [InlineData("Access", "Purchasing")]
    [InlineData("Purchasing", "Inventory")]
    [InlineData("Inventory", "Purchasing")]
    [InlineData("Purchasing", "Sales")]
    [InlineData("Sales", "Purchasing")]
    [InlineData("Purchasing", "Wholesale.Api")]
    [InlineData("Access", "Sales")]
    [InlineData("Sales", "Access")]
    [InlineData("Inventory", "Sales")]
    [InlineData("Sales", "Inventory")]
    [InlineData("Sales", "Wholesale.Api")]
    [InlineData("Access", "Inventory")]
    [InlineData("Inventory", "Access")]
    [InlineData("Access", "Wholesale.Api")]
    [InlineData("Inventory", "Wholesale.Api")]
    public void ModuleImplementationsDoNotDependOnPeersOrTheHost(string module, string forbidden) =>
        NoDependency(ReflectionAssembly.Load(module), ReflectionAssembly.Load(forbidden));

    [Theory]
    [InlineData("Dispatch.Contracts")]
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
                typeof(Gloaming.Messaging.RabbitMQ.RabbitMqSession).Assembly,
                typeof(RabbitMQ.Client.IConnection).Assembly,
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
            typeof(Samples.Wholesale.Worker.Broker.WorkflowComposition).Assembly,
            typeof(Samples.Wholesale.Worker.Local.LocalWorkflowComposition).Assembly,
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
            .ResideInNamespace("ModulithFoundry.Samples.Wholesale.Api.Endpoints");
        Assert.NotEmpty(endpoints.GetObjects(Architecture));
        foreach (ReflectionAssembly forbidden in Modules)
            endpoints
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(forbidden))
                .Check(Architecture);
    }

    [Theory]
    [InlineData("ModulithFoundry.Samples.Wholesale.Worker.Local.Delivery")]
    [InlineData("ModulithFoundry.Samples.Wholesale.Worker.Directory.Delivery")]
    public void RelaysUseContractsWithoutModulePersistenceOrBrokerDependencies(string namespaceName)
    {
        var relays = Types().That().ResideInNamespace(namespaceName);
        Assert.NotEmpty(relays.GetObjects(Architecture));
        foreach (ReflectionAssembly forbidden in Modules.Append(typeof(DbContext).Assembly))
            relays
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(forbidden))
                .Check(Architecture);

        foreach (
            ReflectionAssembly broker in new[]
            {
                typeof(Gloaming.Messaging.RabbitMQ.RabbitMqSession).Assembly,
                typeof(RabbitMQ.Client.IConnection).Assembly,
            }
        )
            relays
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(broker))
                .Check(Architecture);
    }

    [Theory]
    [InlineData("ModulithFoundry.Samples.Wholesale.Worker.Local")]
    [InlineData("ModulithFoundry.Samples.Wholesale.Worker.Local.Delivery")]
    [InlineData("ModulithFoundry.Samples.Wholesale.Worker.Directory")]
    [InlineData("ModulithFoundry.Samples.Wholesale.Worker.Directory.Delivery")]
    public void EveryLocalTransportComponentRemainsIndependentOfBrokerApis(string namespaceName)
    {
        var components = Types().That().ResideInNamespace(namespaceName);
        Assert.NotEmpty(components.GetObjects(Architecture));
        foreach (
            var broker in new[]
            {
                typeof(Gloaming.Messaging.RabbitMQ.RabbitMqSession).Assembly,
                typeof(RabbitMQ.Client.IConnection).Assembly,
            }
        )
            components
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(broker))
                .Check(Architecture);
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
