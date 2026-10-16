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
    private const string AuditPostgres = "Gloaming.Auditing.EntityFrameworkCore.Postgres";
    private const string ActorHttp = "Gloaming.ActorIdentity.AspNetCore";
    private const string Tenancy = "Gloaming.Tenancy";
    private const string TenancyHttp = "Gloaming.Tenancy.AspNetCore";
    private const string TenancyPostgres = "Gloaming.Tenancy.EntityFrameworkCore.Postgres";
    private const string Persistence = "Gloaming.Persistence.EntityFrameworkCore";
    private const string ContextSample = "ContextDemo";
    private const string PersistenceSample = "PersistenceDemo";
    private const string HttpSample = "HttpIdentityDemo";
    private const string EventSerialization = "Gloaming.Events.Serialization";
    private const string History = "Gloaming.Events.History";
    private const string EventPersistenceSample = "EventPersistenceDemo";
    private const string AggregateCore = "Gloaming.EventSourcing";
    private const string Storage = "Gloaming.EventSourcing.EntityFrameworkCore";
    private const string StorageSample = "EventStorageDemo";
    private const string CodecSample = "EventCodecDemo";

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(ActorId).Assembly,
            typeof(Gloaming.Auditing.EntityFrameworkCore.AuditRecord).Assembly,
            typeof(Gloaming.Auditing.EntityFrameworkCore.Postgres.PostgresAuditModelExtensions).Assembly,
            typeof(Gloaming.Messaging.OutgoingMessage).Assembly,
            typeof(Gloaming.Messaging.Workflows.WorkflowStatus).Assembly,
            typeof(Gloaming.Messaging.Workflows.EntityFrameworkCore.WorkflowModelBuilderExtensions).Assembly,
            typeof(Gloaming.Messaging.EntityFrameworkCore.IOutbox<>).Assembly,
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
            typeof(Samples.Wholesale.EventPersistenceDemo.DemoJourneys).Assembly,
            typeof(Samples.Wholesale.ContextDemo.DemoComposition).Assembly,
            typeof(Samples.Wholesale.PersistenceDemo.DemoComposition).Assembly,
            typeof(Samples.Wholesale.HttpIdentityDemo.DemoComposition).Assembly
        )
        .Build();

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
                AuditPostgres,
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
                AuditPostgres,
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
    public void WorkflowCoreAndRunnerRemainOptionalWithoutTransportOrModuleDependencies()
    {
        foreach (
            string library in new[]
            {
                "Gloaming.Messaging.Workflows",
                "Gloaming.Messaging.Workflows.EntityFrameworkCore",
            }
        )
        {
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
                }
            )
                NoDependency(library, forbidden).Check(Architecture);
        }

        Assert.DoesNotContain(
            typeof(Gloaming.Messaging.Workflows.WorkflowStatus).Assembly.GetReferencedAssemblies(),
            reference =>
                reference.Name!.StartsWith(
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.Ordinal
                )
                || reference.Name.StartsWith("Npgsql", StringComparison.Ordinal)
                || reference.Name.StartsWith("RabbitMQ", StringComparison.Ordinal)
        );
        NoDependency("Gloaming.Messaging", "Gloaming.Messaging.Workflows").Check(Architecture);
        NoDependency("Gloaming.Messaging.EntityFrameworkCore", "Gloaming.Messaging.Workflows")
            .Check(Architecture);
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
            NoDependency(AuditPostgres, forbidden).Check(Architecture);
            if (forbidden.StartsWith("Gloaming.", StringComparison.Ordinal))
            {
                NoDependency(forbidden, Audit).Check(Architecture);
                NoDependency(forbidden, AuditPostgres).Check(Architecture);
            }
        }

        NoDependency(Audit, AuditPostgres).Check(Architecture);
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
                EventSerialization,
                History,
                AggregateCore,
                Storage,
            }
        )
        {
            NoDependency("OutboxDemo", forbidden).Check(Architecture);
            NoDependency("InboxDemo", forbidden).Check(Architecture);
        }
        NoDependency("InboxDemo", "OutboxDemo").Check(Architecture);
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
