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
using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Access.Persistence;
using Gloaming.Samples.Wholesale.Api;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Tenancy;
using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Gloaming.ArchitectureTests;

public sealed class SampleModuleBoundaryTests
{
    private static readonly ReflectionAssembly[] Modules =
    [
        typeof(AccessDbContext).Assembly,
        typeof(InventoryDbContext).Assembly,
        typeof(SalesDbContext).Assembly,
    ];
    private static readonly ReflectionAssembly[] Contracts =
    [
        typeof(IApplicationAccess).Assembly,
        typeof(IStockCatalog).Assembly,
        typeof(ICustomerProfiles).Assembly,
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
            .ResideInNamespace("Gloaming.Samples.Wholesale.Api.Endpoints");
        Assert.NotEmpty(endpoints.GetObjects(Architecture));
        foreach (ReflectionAssembly forbidden in Modules)
            endpoints
                .Should()
                .NotDependOnAny(Types().That().ResideInAssembly(forbidden))
                .Check(Architecture);
    }

    [Fact]
    public void RelaysUseContractsWithoutModulePersistenceOrBrokerDependencies()
    {
        var relays = Types()
            .That()
            .ResideInNamespace("Gloaming.Samples.Wholesale.Worker.Local.Delivery");
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
    [InlineData("Gloaming.Samples.Wholesale.Worker.Local")]
    [InlineData("Gloaming.Samples.Wholesale.Worker.Local.Delivery")]
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
