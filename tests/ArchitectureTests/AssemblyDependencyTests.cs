using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
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
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ReflectionAssembly = System.Reflection.Assembly;

namespace ModulithFoundry.ArchitectureTests;

public sealed class AssemblyDependencyTests
{
    private const string Actor = "Gloaming.ActorIdentity";
    private const string Audit = "Gloaming.Auditing.EntityFrameworkCore";
    private const string ActorHttp = "Gloaming.ActorIdentity.AspNetCore";
    private const string Tenancy = "Gloaming.Tenancy";
    private const string TenancyHttp = "Gloaming.Tenancy.AspNetCore";
    private const string TenancyPostgres = "Gloaming.Tenancy.EntityFrameworkCore.Postgres";
    private const string Persistence = "Gloaming.Persistence.EntityFrameworkCore";
    private const string ContextSample = "ContextDemo";
    private const string PersistenceSample = "PersistenceDemo";
    private const string HttpSample = "Wholesale.Api";
    private const string EventSerialization = "Gloaming.Events.Serialization";
    private const string History = "Gloaming.Events.History";
    private const string EventPersistenceSample = "Wholesale.Worker";
    private const string AggregateCore = "Gloaming.EventSourcing";
    private const string Storage = "Gloaming.EventSourcing.EntityFrameworkCore";
    private const string StorageSample = "EventStorageDemo";
    private const string CodecSample = "EventCodecDemo";

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(ActorId).Assembly,
            typeof(Gloaming.Auditing.EntityFrameworkCore.AuditRecord).Assembly,
            typeof(Gloaming.Messaging.OutgoingMessage).Assembly,
            typeof(Gloaming.Messaging.RabbitMQ.RabbitMqSession).Assembly,
            typeof(Gloaming.Messaging.Workflows.WorkflowStatus).Assembly,
            typeof(Gloaming.Messaging.EntityFrameworkCore.IOutbox<>).Assembly,
            typeof(Gloaming.Messaging.EntityFrameworkCore.Hosting.OutboxWorkerOptions).Assembly,
            typeof(Gloaming.Messaging.EntityFrameworkCore.Postgres.PostgresOutboxDispatcher<>).Assembly,
            typeof(Samples.OutboxDemo.DemoJourneys).Assembly,
            typeof(Samples.InboxDemo.InboxDemoHost).Assembly,
            typeof(Samples.MessagingDemo.MessagingJourney).Assembly,
            typeof(Samples.MessagingProducerDemo.ProducerAssemblyMarker).Assembly,
            typeof(Samples.MessagingWorkerDemo.WorkerAssemblyMarker).Assembly,
            typeof(ActorContextMiddleware).Assembly,
            typeof(TenantId).Assembly,
            typeof(TenantContextMiddleware).Assembly,
            typeof(TenantDatabaseContextExtensions).Assembly,
            typeof(Gloaming.EntityFrameworkCore.CapabilityConfiguration).Assembly,
            typeof(Samples.Wholesale.Sales.SalesDatabase).Assembly,
            typeof(Samples.Wholesale.Access.AccessRegistration).Assembly,
            typeof(TenantOwnershipExtensions).Assembly,
            typeof(Gloaming.Persistence.EntityFrameworkCore.Postgres.TenantRowSecurityExtensions).Assembly,
            typeof(SerializedEvent).Assembly,
            typeof(EventHistory).Assembly,
            typeof(IEventStreamRecord).Assembly,
            typeof(IEventSourcedAggregate<>).Assembly,
            typeof(Samples.EventStorageDemo.DemoJourneys).Assembly,
            typeof(Samples.Wholesale.EventCodecDemo.DemoJourneys).Assembly,
            typeof(Samples.Wholesale.Worker.Maintenance.StockRegeneration).Assembly,
            typeof(Samples.Wholesale.ContextDemo.DemoComposition).Assembly,
            typeof(Samples.Wholesale.PersistenceDemo.DemoComposition).Assembly,
            typeof(Samples.Wholesale.Api.DemoComposition).Assembly
        )
        .Build();

    [Fact]
    public void WorkerHostingRemainsIndependentOfProviderAndOptionalForCallableLibraries()
    {
        const string hosting = "Gloaming.Messaging.EntityFrameworkCore.Hosting";
        foreach (
            string callable in new[]
            {
                "Gloaming.Messaging",
                "Gloaming.Messaging.EntityFrameworkCore",
                "Gloaming.Messaging.EntityFrameworkCore.Postgres",
                "Gloaming.Messaging.Workflows.EntityFrameworkCore",
                "Gloaming.EntityFrameworkCore",
            }
        )
            NoDependency(callable, hosting).Check(Architecture);

        foreach (
            string forbidden in new[]
            {
                "Gloaming.Messaging.EntityFrameworkCore.Postgres",
                "Gloaming.Messaging.Workflows.EntityFrameworkCore",
                "Gloaming.Messaging.RabbitMQ",
                "Sales",
                "Access",
                "OutboxDemo",
                "InboxDemo",
            }
        )
            NoDependency(hosting, forbidden).Check(Architecture);
    }

    [Fact]
    public void PostgresOwnershipIntegrationIsOptionalWithoutApplicationAuthorizationDependencies()
    {
        const string adapter = "Gloaming.Persistence.EntityFrameworkCore.Postgres";
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                TenancyHttp,
                Audit,
                "Sales",
                "Access",
                "Gloaming.Messaging",
            }
        )
            NoDependency(adapter, forbidden).Check(Architecture);

        foreach (
            string independent in new[]
            {
                Tenancy,
                TenancyPostgres,
                Persistence,
                "Gloaming.EntityFrameworkCore",
            }
        )
            NoDependency(independent, adapter).Check(Architecture);
    }

    [Fact]
    public void PostgresTenantContextRemainsOptionalAndUsesOnlyTenancyAndNativeStorage()
    {
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                TenancyHttp,
                Persistence,
                Audit,
                ContextSample,
                PersistenceSample,
                HttpSample,
                "Gloaming.EntityFrameworkCore",
                "Gloaming.Messaging",
                "Gloaming.Messaging.EntityFrameworkCore",
                "Sales",
                "Access",
            }
        )
            NoDependency(TenancyPostgres, forbidden).Check(Architecture);

        NoDependency(Tenancy, TenancyPostgres).Check(Architecture);
        NoDependency(Persistence, TenancyPostgres).Check(Architecture);
    }

    [Fact]
    public void ContinuationsRemainOptionalWithoutTransportHostingOrModuleDependencies()
    {
        const string library = "Gloaming.Messaging.Workflows.EntityFrameworkCore";
        foreach (
            string forbidden in new[]
            {
                Actor,
                Tenancy,
                Persistence,
                EventSerialization,
                History,
                AggregateCore,
                Storage,
                "Gloaming.Messaging.EntityFrameworkCore.Postgres",
                "Gloaming.Messaging.EntityFrameworkCore.Hosting",
                "Gloaming.Messaging.RabbitMQ",
                "Sales",
                "Access",
            }
        )
            NoDependency(library, forbidden).Check(Architecture);

        Assert.DoesNotContain(
            typeof(Gloaming.Messaging.Workflows.WorkflowStatus).Assembly.GetReferencedAssemblies(),
            reference =>
                reference.Name!.StartsWith("Npgsql", StringComparison.Ordinal)
                || reference.Name.StartsWith(
                    "Microsoft.Extensions.Hosting",
                    StringComparison.Ordinal
                )
                || reference.Name.StartsWith("RabbitMQ", StringComparison.Ordinal)
        );
        foreach (
            string independent in new[]
            {
                "Gloaming.Messaging",
                "Gloaming.EntityFrameworkCore",
                "Gloaming.Messaging.EntityFrameworkCore",
                "Gloaming.Messaging.EntityFrameworkCore.Postgres",
                "Gloaming.Messaging.EntityFrameworkCore.Hosting",
            }
        )
            NoDependency(independent, library).Check(Architecture);
    }

    [Theory]
    [InlineData(Actor, Audit)]
    [InlineData(Tenancy, Audit)]
    [InlineData(Persistence, Audit)]
    [InlineData(Actor, Tenancy)]
    [InlineData(Tenancy, Actor)]
    [InlineData(Persistence, Actor)]
    [InlineData(Persistence, Tenancy)]
    [InlineData(Actor, ContextSample)]
    [InlineData(Actor, PersistenceSample)]
    [InlineData(Tenancy, ContextSample)]
    [InlineData(Tenancy, PersistenceSample)]
    [InlineData(Persistence, ContextSample)]
    [InlineData(Persistence, PersistenceSample)]
    [InlineData(ActorHttp, Tenancy)]
    [InlineData(ActorHttp, Persistence)]
    [InlineData(ActorHttp, ContextSample)]
    [InlineData(ActorHttp, PersistenceSample)]
    [InlineData(ActorHttp, HttpSample)]
    [InlineData(Actor, HttpSample)]
    [InlineData(Tenancy, HttpSample)]
    [InlineData(Persistence, HttpSample)]
    [InlineData(TenancyHttp, Actor)]
    [InlineData(TenancyHttp, ActorHttp)]
    [InlineData(TenancyHttp, Persistence)]
    [InlineData(TenancyHttp, ContextSample)]
    [InlineData(TenancyHttp, PersistenceSample)]
    [InlineData(TenancyHttp, HttpSample)]
    [InlineData(ActorHttp, TenancyHttp)]
    [InlineData(Actor, TenancyHttp)]
    [InlineData(Tenancy, TenancyHttp)]
    [InlineData(Persistence, TenancyHttp)]
    public void LibrariesDoNotUseForbiddenSegmentsOrConsumerTypes(
        string library,
        string forbidden
    ) => NoDependency(library, forbidden).Check(Architecture);

    [Fact]
    public void AuditUsesActorValuesAndNativeEfWithoutOtherSegmentsOrConsumers()
    {
        foreach (
            string forbidden in new[]
            {
                ActorHttp,
                Tenancy,
                TenancyHttp,
                Persistence,
                EventSerialization,
                History,
                AggregateCore,
                Storage,
                ContextSample,
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
                StorageSample,
                "Gloaming.Messaging",
                "Gloaming.Messaging.EntityFrameworkCore",
                "Gloaming.Messaging.EntityFrameworkCore.Postgres",
                "OutboxDemo",
                "InboxDemo",
                "MessagingDemo",
            }
        )
        {
            NoDependency(Audit, forbidden).Check(Architecture);
            if (forbidden.StartsWith("Gloaming.", StringComparison.Ordinal))
            {
                NoDependency(forbidden, Audit).Check(Architecture);
            }
        }

        Assert.DoesNotContain(
            typeof(Gloaming.Auditing.EntityFrameworkCore.AuditRecord).Assembly.GetReferencedAssemblies(),
            assembly =>
                assembly.Name!.StartsWith("Npgsql", StringComparison.Ordinal)
                || assembly.Name.StartsWith("RabbitMQ", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void EventSerializationUsesNoOtherSegmentsOrConsumerTypes()
    {
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                Tenancy,
                TenancyHttp,
                Persistence,
                ContextSample,
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
                AggregateCore,
                Storage,
                StorageSample,
                History,
            }
        )
            NoDependency(EventSerialization, forbidden).Check(Architecture);
    }

    [Fact]
    public void MessagingStaysIndependentAndProviderCodeDoesNotLeakIntoItsCoreOrEfLayer()
    {
        string[] layers =
        [
            "Gloaming.Messaging",
            "Gloaming.Messaging.EntityFrameworkCore",
            "Gloaming.Messaging.EntityFrameworkCore.Postgres",
        ];
        foreach (string layer in layers)
        {
            NoDependency(layer, "Gloaming.Messaging.RabbitMQ").Check(Architecture);
            foreach (
                string forbidden in new[]
                {
                    Actor,
                    ActorHttp,
                    Tenancy,
                    TenancyHttp,
                    Persistence,
                    EventSerialization,
                    History,
                    AggregateCore,
                    Storage,
                    ContextSample,
                    PersistenceSample,
                    HttpSample,
                    EventPersistenceSample,
                    "OutboxDemo",
                    "InboxDemo",
                    "MessagingDemo",
                    "MessagingProducerDemo",
                    "MessagingWorkerDemo",
                }
            )
                NoDependency(layer, forbidden).Check(Architecture);
        }
        NoDependency(layers[0], layers[1]).Check(Architecture);
        NoDependency(layers[0], layers[2]).Check(Architecture);
        NoDependency(layers[1], layers[2]).Check(Architecture);
        Assert.DoesNotContain(
            typeof(Gloaming.Messaging.EntityFrameworkCore.IOutbox<>).Assembly.GetReferencedAssemblies(),
            assembly =>
                assembly.Name!.StartsWith("Npgsql", StringComparison.Ordinal)
                || assembly.Name.StartsWith("RabbitMQ", StringComparison.Ordinal)
        );
        foreach (
            string forbidden in new[]
            {
                Actor,
                Tenancy,
                Persistence,
                History,
                AggregateCore,
                Storage,
            }
        )
        {
            NoDependency("OutboxDemo", forbidden).Check(Architecture);
            NoDependency("InboxDemo", forbidden).Check(Architecture);
        }

        // Only the receiving sample opts into historical payload decoding; Messaging stays codec-free.
        NoDependency("OutboxDemo", EventSerialization).Check(Architecture);
        Assert.Contains(
            typeof(Samples.InboxDemo.InboxDemoHost).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == EventSerialization
        );
        NoDependency("InboxDemo", "OutboxDemo").Check(Architecture);
    }

    [Fact]
    public void OptionalRabbitMqLifecycleReferencesOnlyNativeCodeAndDoesNotLeakIntoModules()
    {
        var integration = typeof(Gloaming.Messaging.RabbitMQ.RabbitMqSession).Assembly;
        Assert.All(
            integration.GetReferencedAssemblies(),
            reference =>
                Assert.True(
                    reference.Name!.StartsWith("System.", StringComparison.Ordinal)
                        || reference.Name
                            is "RabbitMQ.Client"
                                or "Microsoft.Extensions.Logging.Abstractions",
                    reference.FullName
                )
        );
        foreach (
            var module in new[]
            {
                typeof(Samples.OutboxDemo.ExportDbContext).Assembly,
                typeof(Samples.InboxDemo.RenderDbContext).Assembly,
                typeof(Samples.MessagingProducerDemo.ProducerAssemblyMarker).Assembly,
                typeof(Samples.Wholesale.Sales.SalesDbContext).Assembly,
                typeof(Samples.Wholesale.Inventory.InventoryDbContext).Assembly,
                typeof(Samples.Wholesale.Dispatch.DispatchDbContext).Assembly,
            }
        )
            Assert.DoesNotContain(
                module.GetReferencedAssemblies(),
                reference => reference.Name == integration.GetName().Name
            );
    }

    [Fact]
    public void ProducerDoesNotHostMessagingWorkersAndModulesDoNotDependOnTheirHosts()
    {
        foreach (
            string forbidden in new[]
            {
                "InboxDemo",
                "MessagingDemo",
                "MessagingWorkerDemo",
                "RabbitMQ.Client",
            }
        )
            Assert.DoesNotContain(
                typeof(Samples.MessagingProducerDemo.ProducerAssemblyMarker).Assembly.GetReferencedAssemblies(),
                reference => reference.Name == forbidden
            );

        foreach (string module in new[] { "OutboxDemo", "InboxDemo", "MessagingDemo" })
        {
            NoDependency(module, "MessagingProducerDemo").Check(Architecture);
            NoDependency(module, "MessagingWorkerDemo").Check(Architecture);
        }
    }

    [Fact]
    public void EventHistoryUsesNoOtherSegmentsOrConsumerTypes()
    {
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                Tenancy,
                TenancyHttp,
                Persistence,
                EventSerialization,
                ContextSample,
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
                AggregateCore,
                Storage,
                StorageSample,
            }
        )
            NoDependency(History, forbidden).Check(Architecture);
    }

    [Fact]
    public void AggregateCoreUsesNoOtherSegmentsOrConsumerTypes()
    {
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                Tenancy,
                TenancyHttp,
                Persistence,
                EventSerialization,
                History,
                Storage,
                ContextSample,
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
                StorageSample,
            }
        )
            NoDependency(AggregateCore, forbidden).Check(Architecture);
        Assert.DoesNotContain(
            typeof(IEventSourcedAggregate<>).Assembly.GetReferencedAssemblies(),
            assembly =>
                assembly.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || assembly.Name == "System.Text.Json"
        );
    }

    [Fact]
    public void EventStorageAllowsHistoryIntegrityButNoOtherSegmentsOrConsumers()
    {
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                Tenancy,
                TenancyHttp,
                Persistence,
                EventSerialization,
                ContextSample,
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
                StorageSample,
            }
        )
            NoDependency(Storage, forbidden).Check(Architecture);
    }

    [Fact]
    public void IndependentStorageConsumerAllowsHistoryErrorsButNoOtherSegmentsOrSamples()
    {
        foreach (
            string forbidden in new[]
            {
                Actor,
                ActorHttp,
                Tenancy,
                TenancyHttp,
                Persistence,
                EventSerialization,
                ContextSample,
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
            }
        )
            NoDependency(StorageSample, forbidden).Check(Architecture);
    }

    private static TypesShouldConjunction NoDependency(string source, string forbidden)
    {
        var sourceTypes = Types().That().ResideInAssembly(ReflectionAssembly.Load(source));
        var forbiddenTypes = Types().That().ResideInAssembly(ReflectionAssembly.Load(forbidden));
        Assert.NotEmpty(sourceTypes.GetObjects(Architecture));
        Assert.NotEmpty(forbiddenTypes.GetObjects(Architecture));
        return sourceTypes.Should().NotDependOnAny(forbiddenTypes);
    }
}
