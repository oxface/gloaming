using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.EventStorageDemo.Tests;

public sealed class KeyConfigurationTests
{
    [Fact]
    public void MatchingOwnershipPrefixesCanUseDifferentConsumerPropertyNames()
    {
        var options = new DbContextOptionsBuilder<MatchingContext>()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .Options;
        using var database = new MatchingContext(options);
        var relationship = Assert.Single(
            database.Model.FindEntityType(typeof(OwnedEvent))!.GetForeignKeys()
        );
        Assert.Equal(
            ["Owner", "Id"],
            relationship.PrincipalKey.Properties.Select(property => property.Name)
        );
        Assert.Equal(
            ["Partition", "StreamId"],
            relationship.Properties.Select(property => property.Name)
        );
    }

    [Theory]
    [InlineData("header-order")]
    [InlineData("reference-owner")]
    [InlineData("event-owner")]
    public void MismatchedIdentitiesFailBeforeTheConsumerCanUseTheModel(string fault)
    {
        var options = new DbContextOptionsBuilder<MalformedContext>()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .Options;
        using var database = new MalformedContext(options, fault);
        Assert.Throws<InvalidOperationException>(() => database.Model);
    }

    private sealed class MatchingContext(DbContextOptions<MatchingContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventSourcingStorage<OwnedStream, OwnedEvent>(
                row => new { row.Owner, row.Id },
                row => new { row.Partition, row.EventId },
                row => new { row.Partition, row.StreamId }
            );
            modelBuilder.Entity<OwnedEvent>().Property(row => row.Payload).HasColumnType("jsonb");
        }
    }

    // Every malformed variant fails model construction, so none can enter EF's model cache.
    private sealed class MalformedContext(DbContextOptions<MalformedContext> options, string fault)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventSourcingStorage<OwnedStream, OwnedEvent>(
                fault == "header-order"
                    ? row => new { row.Id, row.Owner }
                    : row => new { row.Owner, row.Id },
                fault == "event-owner"
                    ? row => row.EventId
                    : row => new { row.Partition, row.EventId },
                fault == "reference-owner"
                    ? row => row.StreamId
                    : row => new { row.Partition, row.StreamId }
            );
            modelBuilder.Entity<OwnedEvent>().Property(row => row.Payload).HasColumnType("jsonb");
        }
    }

    private sealed class OwnedStream : IEventStreamRecord
    {
        public string Owner { get; set; } = null!;
        public Guid Id { get; set; }
        public string StreamType { get; set; } = null!;
        public long Version { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class OwnedEvent : IStoredEventRecord
    {
        public string Partition { get; set; } = null!;
        public Guid EventId { get; set; }
        public Guid StreamId { get; set; }
        public long StreamVersion { get; set; }
        public string EventName { get; set; } = null!;
        public int SchemaVersion { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public JsonElement Payload { get; set; }
    }
}
