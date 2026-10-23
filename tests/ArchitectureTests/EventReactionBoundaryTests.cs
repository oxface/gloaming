using System.Xml.Linq;
using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using ModulithFoundry.Samples.EventReactions;
using ModulithFoundry.Samples.EventReactions.Model;

namespace ModulithFoundry.ArchitectureTests;

public sealed class EventReactionBoundaryTests
{
    [Fact]
    public void SharedEfIntegrationHasNoFeatureOrProviderDependency()
    {
        var declaration = Declaration("Gloaming.EntityFrameworkCore");
        Assert.Empty(declaration.Descendants("ProjectReference"));
        Assert.Empty(declaration.Descendants("FrameworkReference"));
        Assert.Equal(
            [
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
            ],
            declaration
                .Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
        );
    }

    [Fact]
    public void CoreDeclaresNoPackageFrameworkOrProjectDependency()
    {
        var declaration = Declaration("Gloaming.EventReactions");
        Assert.Empty(declaration.Descendants("PackageReference"));
        Assert.Empty(declaration.Descendants("FrameworkReference"));
        Assert.Empty(declaration.Descendants("ProjectReference"));
        Assert.All(
            typeof(PendingEvents).Assembly.GetReferencedAssemblies(),
            reference => Assert.StartsWith("System.", reference.Name!, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void EfAdapterDeclaresOnlyCoreAndNativeRelationalEf()
    {
        var declaration = Declaration("Gloaming.EventReactions.EntityFrameworkCore");
        Assert.Equal(
            ["Gloaming.EntityFrameworkCore", "Gloaming.EventReactions"],
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
        Assert.All(
            typeof(EventReactionServiceCollectionExtensions).Assembly.GetReferencedAssemblies(),
            reference =>
                Assert.True(
                    reference.Name is "Gloaming.EventReactions" or "Gloaming.EntityFrameworkCore"
                        || reference.Name!.StartsWith("System.", StringComparison.Ordinal)
                        || reference.Name.StartsWith(
                            "Microsoft.Extensions.DependencyInjection",
                            StringComparison.Ordinal
                        )
                        || reference.Name.StartsWith(
                            "Microsoft.EntityFrameworkCore",
                            StringComparison.Ordinal
                        ),
                    reference.Name
                )
        );
    }

    [Fact]
    public void IndependentConsumerUsesReactionsWithoutWholesaleOrOtherGloamingFamilies()
    {
        var dependencies = typeof(ReactionExample).Assembly.GetReferencedAssemblies();
        Assert.Contains(
            dependencies,
            reference =>
                reference.Name is "Gloaming.EventReactions" or "Gloaming.EntityFrameworkCore"
        );
        Assert.Contains(
            dependencies,
            reference => reference.Name == "Gloaming.EventReactions.EntityFrameworkCore"
        );
        Assert.DoesNotContain(
            dependencies,
            reference =>
                reference.Name is "Sales" or "Sales.Contracts" or "Inventory" or "Access"
                || reference.Name!.StartsWith("Gloaming.", StringComparison.Ordinal)
                    && reference.Name
                        is not "Gloaming.EventReactions"
                            and not "Gloaming.EventReactions.EntityFrameworkCore"
                            and not "Gloaming.EntityFrameworkCore"
        );
    }

    [Fact]
    public void ConsumerModelNeedsNoReactionMarkerBaseOrInfrastructureDependency()
    {
        var declaration = Declaration("EventReactionsDemo.Model");
        Assert.Empty(declaration.Descendants("PackageReference"));
        Assert.Empty(declaration.Descendants("FrameworkReference"));
        Assert.Empty(declaration.Descendants("ProjectReference"));
        Assert.All(
            typeof(QuoteDomain).Assembly.GetReferencedAssemblies(),
            reference => Assert.StartsWith("System.", reference.Name!, StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            typeof(QuoteAccepted).GetInterfaces(),
            contract => contract.Assembly == typeof(PendingEvents).Assembly
        );
    }

    private static XDocument Declaration(string project) =>
        XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "ProjectDeclarations", project + ".csproj")
        );
}
