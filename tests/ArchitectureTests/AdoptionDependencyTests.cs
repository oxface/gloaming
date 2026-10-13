using System.Xml.Linq;

namespace ModulithFoundry.ArchitectureTests;

public sealed class AdoptionDependencyTests
{
    [Theory]
    [InlineData(
        "Rootbolt.Auditing.EntityFrameworkCore",
        "Rootbolt.ActorIdentity",
        "Microsoft.EntityFrameworkCore.Relational"
    )]
    [InlineData(
        "Rootbolt.Auditing.EntityFrameworkCore.Postgres",
        "Rootbolt.Auditing.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore.PostgreSQL"
    )]
    [InlineData(
        "AuditPostgresTests",
        "Rootbolt.Auditing.EntityFrameworkCore.Postgres",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Testcontainers.PostgreSql",
        "xunit.v3.mtp-v2"
    )]
    public void AuditLibraryAndIndependentConsumerDeclareOnlyUsedDependencies(
        string project,
        string dependency,
        params string[] packages
    )
    {
        var declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", project + ".csproj")
        );
        Assert.Equal(
            [dependency],
            declaration
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            packages.Order(StringComparer.Ordinal),
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
        Assert.Empty(declaration.Descendants("FrameworkReference"));
    }

    [Theory]
    [InlineData("Rootbolt.ActorIdentity.AspNetCore", "Rootbolt.ActorIdentity")]
    [InlineData("Rootbolt.Tenancy.AspNetCore", "Rootbolt.Tenancy")]
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
    [InlineData("Rootbolt.ActorIdentity", null)]
    [InlineData("Rootbolt.Tenancy", null)]
    [InlineData("Rootbolt.Events.Serialization", null)]
    [InlineData("Rootbolt.Events.History", null)]
    [InlineData("Rootbolt.Messaging", null)]
    [InlineData("Rootbolt.Messaging.OpenTelemetry", "OpenTelemetry.Api")]
    [InlineData(
        "Rootbolt.Persistence.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational"
    )]
    [InlineData("Rootbolt.EventSourcing", null)]
    public void RuntimeLibrariesDeclareOnlyTheirIntendedDependencies(
        string library,
        string? package
    )
    {
        XDocument declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", library + ".csproj")
        );
        Assert.Empty(declaration.Descendants("ProjectReference"));
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
                "Rootbolt.EventSourcing.EntityFrameworkCore.csproj"
            )
        );
        Assert.Equal(
            ["Rootbolt.EventSourcing", "Rootbolt.Events.History"],
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
        Check("Rootbolt.Messaging.Workflows", ["Rootbolt.Messaging"], []);
        Check(
            "Rootbolt.Messaging.Workflows.EntityFrameworkCore",
            ["Rootbolt.Messaging.Workflows", "Rootbolt.Messaging.EntityFrameworkCore"],
            [
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
            ]
        );
        Check(
            "Rootbolt.Messaging.EntityFrameworkCore",
            ["Rootbolt.Messaging"],
            [
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "Microsoft.Extensions.Hosting.Abstractions",
                "Microsoft.Extensions.Logging.Abstractions",
            ]
        );
        Check(
            "Rootbolt.Messaging.EntityFrameworkCore.Postgres",
            ["Rootbolt.Messaging.EntityFrameworkCore"],
            ["Microsoft.Extensions.Logging.Abstractions", "Npgsql.EntityFrameworkCore.PostgreSQL"]
        );
        Check(
            "OutboxDemo",
            ["Rootbolt.Messaging.EntityFrameworkCore.Postgres"],
            ["Microsoft.EntityFrameworkCore.Design"]
        );
        Check(
            "InboxDemo",
            ["Rootbolt.Messaging.EntityFrameworkCore.Postgres"],
            ["Microsoft.EntityFrameworkCore.Design"]
        );

        static void Check(string project, string[] projects, string[] packages)
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
            Assert.Empty(declaration.Descendants("FrameworkReference"));
        }
    }

    [Fact]
    public void IndependentStorageConsumerDeclaresOnlyStorageAndNativeProviderPackages()
    {
        XDocument declaration = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", "EventStorageDemo.csproj")
        );
        Assert.Equal(
            ["Rootbolt.EventSourcing", "Rootbolt.EventSourcing.EntityFrameworkCore"],
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

    [Fact]
    public void StandalonePostgresConsumerDeclaresOnlyItsSelectedDependencies()
    {
        var consumer = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "ProjectDeclarations",
                "EventSourcingPostgresTests.csproj"
            )
        );
        Assert.Equal(
            ["Rootbolt.EventSourcing.EntityFrameworkCore"],
            consumer
                .Descendants("ProjectReference")
                .Select(reference =>
                    Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!)
                )
        );
        Assert.Equal(
            [
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Testcontainers.PostgreSql",
                "xunit.v3.mtp-v2",
            ],
            consumer
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal)
        );
    }
}
