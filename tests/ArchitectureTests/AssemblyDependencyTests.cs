using System.Text.Json;
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
            typeof(Samples.Messaging.Exports.ExportDbContext).Assembly,
            typeof(Samples.Messaging.Rendering.RenderDbContext).Assembly,
            typeof(Samples.Messaging.Producer.ProducerAssemblyMarker).Assembly,
            typeof(Samples.Messaging.Worker.WorkerAssemblyMarker).Assembly,
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
            typeof(Samples.EventStorageDemo.CounterCommands).Assembly,
            typeof(Samples.Wholesale.EventCodecDemo.CodecExample).Assembly,
            typeof(Samples.Wholesale.Worker.Maintenance.StockRegeneration).Assembly,
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
                "Messaging.Exports",
                "Messaging.Rendering",
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
    [InlineData(Actor, PersistenceSample)]
    [InlineData(Tenancy, PersistenceSample)]
    [InlineData(Persistence, PersistenceSample)]
    [InlineData(ActorHttp, Tenancy)]
    [InlineData(ActorHttp, Persistence)]
    [InlineData(ActorHttp, PersistenceSample)]
    [InlineData(ActorHttp, HttpSample)]
    [InlineData(Actor, HttpSample)]
    [InlineData(Tenancy, HttpSample)]
    [InlineData(Persistence, HttpSample)]
    [InlineData(TenancyHttp, Actor)]
    [InlineData(TenancyHttp, ActorHttp)]
    [InlineData(TenancyHttp, Persistence)]
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
                PersistenceSample,
                HttpSample,
                CodecSample,
                EventPersistenceSample,
                StorageSample,
                "Gloaming.Messaging",
                "Gloaming.Messaging.EntityFrameworkCore",
                "Gloaming.Messaging.EntityFrameworkCore.Postgres",
                "Messaging.Exports",
                "Messaging.Rendering",
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
                    PersistenceSample,
                    HttpSample,
                    EventPersistenceSample,
                    "Messaging.Exports",
                    "Messaging.Rendering",
                    "Messaging.Producer",
                    "Messaging.Worker",
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
            NoDependency("Messaging.Exports", forbidden).Check(Architecture);
            NoDependency("Messaging.Rendering", forbidden).Check(Architecture);
        }

        // Only the receiving sample opts into historical payload decoding; Messaging stays codec-free.
        NoDependency("Messaging.Exports", EventSerialization).Check(Architecture);
        Assert.Contains(
            typeof(Samples.Messaging.Rendering.RenderDbContext).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == EventSerialization
        );
        NoDependency("Messaging.Rendering", "Messaging.Exports").Check(Architecture);
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
                typeof(Samples.Messaging.Exports.ExportDbContext).Assembly,
                typeof(Samples.Messaging.Rendering.RenderDbContext).Assembly,
                typeof(Samples.Messaging.Producer.ProducerAssemblyMarker).Assembly,
                typeof(Samples.Wholesale.Sales.SalesDbContext).Assembly,
                typeof(Samples.Wholesale.Inventory.InventoryDbContext).Assembly,
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
                "Messaging.Rendering",
                "Messaging.Worker",
                "Gloaming.Messaging.EntityFrameworkCore.Hosting",
                "RabbitMQ.Client",
            }
        )
            Assert.DoesNotContain(
                typeof(Samples.Messaging.Producer.ProducerAssemblyMarker).Assembly.GetReferencedAssemblies(),
                reference => reference.Name == forbidden
            );

        foreach (string module in new[] { "Messaging.Exports", "Messaging.Rendering" })
        {
            NoDependency(module, "Messaging.Producer").Check(Architecture);
            NoDependency(module, "Messaging.Worker").Check(Architecture);
        }

        foreach (
            var module in new[]
            {
                typeof(Samples.Messaging.Exports.ExportDbContext).Assembly,
                typeof(Samples.Messaging.Rendering.RenderDbContext).Assembly,
            }
        )
            Assert.DoesNotContain(
                module.GetReferencedAssemblies(),
                reference =>
                    reference.Name == "Gloaming.Messaging.EntityFrameworkCore.Hosting"
                    || reference.Name!.StartsWith(
                        "Microsoft.Extensions.Hosting",
                        StringComparison.Ordinal
                    )
                    || reference.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            );

        // Check the producer's complete runtime graph: an indirect module dependency
        // previously pulled the optional worker package into this API-only host.
        using var dependencies = JsonDocument.Parse(
            File.ReadAllText(
                Path.ChangeExtension(
                    typeof(Samples.Messaging.Producer.ProducerAssemblyMarker).Assembly.Location,
                    ".deps.json"
                )
            )
        );
        Assert.DoesNotContain(
            dependencies.RootElement.GetProperty("libraries").EnumerateObject(),
            library =>
                library.Name.StartsWith(
                    "Gloaming.Messaging.EntityFrameworkCore.Hosting/",
                    StringComparison.Ordinal
                )
                || library.Name.StartsWith("Gloaming.Messaging.RabbitMQ/", StringComparison.Ordinal)
                || library.Name.StartsWith("RabbitMQ.Client/", StringComparison.Ordinal)
        );
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
