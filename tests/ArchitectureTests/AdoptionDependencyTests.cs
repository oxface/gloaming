using System.Xml.Linq;

namespace Gloaming.ArchitectureTests;

public sealed class AdoptionDependencyTests
{
    [Fact]
    public void CommandReceiptsDeclareOnlySaveCompositionAndTheNativePostgresProvider()
    {
        var declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "Gloaming.CommandReceipts.EntityFrameworkCore.Postgres.csproj"
            )
        );
        Assert.Equal(
            ["Gloaming.EntityFrameworkCore"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void CodecExampleDeclaresOnlySerializationWithoutHistoryOrInfrastructure()
    {
        var declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", "EventCodecDemo.csproj")
        );
        Assert.Equal(
            ["Gloaming.Events.Serialization"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Empty(declaration.Descendants("PackageReference"));
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void ContextCompositionUsesOnlyTheTwoIndependentCoresAndNativeDi()
    {
        var declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "ContextCompositionTests.csproj"
            )
        );
        Assert.Equal(
            ["Gloaming.ActorIdentity", "Gloaming.Tenancy"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Microsoft.Extensions.DependencyInjection", "xunit.v3.mtp-v2"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void RabbitMqLifecycleDeclaresNativeDependenciesWithoutMessagingCoreEfOrHost()
    {
        var declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "Gloaming.Messaging.RabbitMQ.csproj"
            )
        );
        Assert.Equal(
            [],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Microsoft.Extensions.Logging.Abstractions", "RabbitMQ.Client"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void OptionalRlsAdapterDeclaresOnlyOwnershipTenancyAndNativeDriver()
    {
        var declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "Gloaming.Persistence.EntityFrameworkCore.Postgres.csproj"
            )
        );
        Assert.Equal(
            ["Gloaming.Persistence.EntityFrameworkCore", "Gloaming.Tenancy"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Npgsql"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void PostgresTenantContextDeclaresOnlyTenancyAndNativeDatabaseDependencies()
    {
        var declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "Gloaming.Tenancy.EntityFrameworkCore.Postgres.csproj"
            )
        );
        Assert.Equal(
            ["Gloaming.Tenancy"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.Relational", "Npgsql"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void AuditLibraryDeclaresOnlySharedSavingAttributionAndNativeEf()
    {
        var declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "Gloaming.Auditing.EntityFrameworkCore.csproj"
            )
        );
        Assert.Equal(
            ["Gloaming.EntityFrameworkCore", "Gloaming.ActorIdentity"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.Relational"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Theory]
    [InlineData("Gloaming.ActorIdentity.AspNetCore", "Gloaming.ActorIdentity")]
    [InlineData("Gloaming.Tenancy.AspNetCore", "Gloaming.Tenancy")]
    public void HttpAdaptersDeclareOnlyTheirCoreAndNativeFramework(string adapter, string core)
    {
        XDocument declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", adapter + ".csproj")
        );
        Assert.Empty(declaration.Descendants("PackageReference"));
        Assert.Equal(
            ["Microsoft.AspNetCore.App"],
            declaration
                .Descendants("FrameworkReference")
                .Select(reference => (string)reference.Attribute("Include")!)
        );
        Assert.Equal(
            [core],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
    }

    [Theory]
    [InlineData("Gloaming.ActorIdentity", null)]
    [InlineData("Gloaming.Tenancy", null)]
    [InlineData("Gloaming.Events.Serialization", null)]
    [InlineData("Gloaming.Events.History", null)]
    [InlineData("Gloaming.Messaging", null)]
    [InlineData(
        "Gloaming.Persistence.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational"
    )]
    [InlineData("Gloaming.EventSourcing", null)]
    public void RuntimeLibrariesDeclareOnlyTheirIntendedDependencies(
        string library,
        string? package
    )
    {
        XDocument declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", library + ".csproj")
        );
        Assert.Equal(
            library == "Gloaming.Persistence.EntityFrameworkCore"
                ? ["Gloaming.EntityFrameworkCore"]
                : [],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
        string[] packages = declaration
            .Descendants("PackageReference")
            .Select(reference => (string)reference.Attribute("Include")!)
            .ToArray();
        Assert.Equal(package is null ? [] : [package], packages);
    }

    [Fact]
    public void EventAdapterDeclaresAggregateCoreHistoryIntegrityNativeEfAndDiAbstractions()
    {
        XDocument declaration = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "Gloaming.EventSourcing.EntityFrameworkCore.csproj"
            )
        );
        Assert.Equal(
            ["Gloaming.EntityFrameworkCore", "Gloaming.EventSourcing", "Gloaming.Events.History"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            [
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
            ],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Fact]
    public void MessagingLayersDeclareOnlyUsedContractsAndNativeDependencies()
    {
        Check(
            "Gloaming.Messaging.Workflows.EntityFrameworkCore",
            [
                "Gloaming.EntityFrameworkCore",
                "Gloaming.Messaging",
                "Gloaming.Messaging.EntityFrameworkCore",
            ],
            [
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
            ]
        );
        Check(
            "Gloaming.Messaging.EntityFrameworkCore",
            ["Gloaming.EntityFrameworkCore", "Gloaming.Messaging"],
            [
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
            ]
        );
        Check(
            "Gloaming.Messaging.EntityFrameworkCore.Hosting",
            ["Gloaming.Messaging.EntityFrameworkCore"],
            [
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "Microsoft.Extensions.Hosting.Abstractions",
                "Microsoft.Extensions.Logging.Abstractions",
            ]
        );
        Check(
            "Gloaming.Messaging.EntityFrameworkCore.Postgres",
            ["Gloaming.Messaging.EntityFrameworkCore"],
            ["Microsoft.Extensions.Logging.Abstractions", "Npgsql.EntityFrameworkCore.PostgreSQL"]
        );
        Check(
            "Messaging.Exports",
            ["Gloaming.Messaging.EntityFrameworkCore.Postgres"],
            ["Microsoft.EntityFrameworkCore.Design"]
        );
        Check(
            "Messaging.Rendering",
            ["Gloaming.Messaging.EntityFrameworkCore.Postgres", "Gloaming.Events.Serialization"],
            ["Microsoft.EntityFrameworkCore.Design"]
        );

        static void Check(
            string project,
            string[] projects,
            string[] packages,
            string[]? frameworks = null
        )
        {
            var declaration = XDocument.Load(
                Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", project + ".csproj")
            );
            Assert.Equal(
                projects.Order(StringComparer.Ordinal),
                declaration
                    .Descendants("ProjectReference")
                    .Select(reference =>
                        Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                    )
                    .Order(StringComparer.Ordinal)
            );
            Assert.Equal(
                packages.Order(StringComparer.Ordinal),
                declaration
                    .Descendants("PackageReference")
                    .Select(reference => (string)reference.Attribute("Include")!)
                    .Order(StringComparer.Ordinal)
            );
            Assert.Equal(
                frameworks ?? [],
                declaration
                    .Descendants("FrameworkReference")
                    .Select(reference => (string)reference.Attribute("Include")!)
            );
        }
    }

    [Fact]
    public void IndependentStorageConsumerDeclaresOnlyStorageAndNativeProviderPackages()
    {
        XDocument declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", "EventStorageDemo.csproj")
        );
        Assert.Equal(
            ["Gloaming.EventSourcing", "Gloaming.EventSourcing.EntityFrameworkCore"],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.Design", "Npgsql.EntityFrameworkCore.PostgreSQL"],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }
}
