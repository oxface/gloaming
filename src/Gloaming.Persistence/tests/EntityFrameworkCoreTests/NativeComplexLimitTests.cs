using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.EntityFrameworkCoreTests;

public sealed class NativeComplexLimitTests
{
    [Theory]
    [InlineData(false, "must be mapped to a JSON column")]
    [InlineData(true, "Map the root complex type to JSON")]
    public void UnsupportedStorageCombinationFailsWithoutAnyOwnershipRegistration(
        bool nestedJson,
        string expectedMessage
    )
    {
        var options = new DbContextOptionsBuilder()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .EnableServiceProviderCaching(false)
            .Options;
        using var context = new NativeContext(options, nestedJson);
        var failure = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.IsNotType<TenantOwnershipException>(failure);
        Assert.Contains(expectedMessage, failure.Message, StringComparison.Ordinal);
    }

    private sealed class NativeContext(DbContextOptions options, bool nestedJson)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<NativeRow>();
            if (nestedJson)
            {
                row.Ignore(x => x.Values);
                row.ComplexProperty(
                    x => x.Inline,
                    inline =>
                        inline.ComplexProperty(x => x.Nested, nested => nested.ToJson("nested"))
                );
            }
            else
            {
                row.Ignore(x => x.Inline);
                row.ComplexCollection(x => x.Values);
            }
        }
    }

    private sealed class NativeRow
    {
        public Guid Id { get; set; }
        public InlineValue Inline { get; set; } = new();
        public List<NestedValue> Values { get; set; } = [];
    }

    private sealed class InlineValue
    {
        public string Label { get; set; } = "Original";
        public NestedValue Nested { get; set; } = new();
    }

    private sealed class NestedValue
    {
        public string Text { get; set; } = "Original";
    }
}
