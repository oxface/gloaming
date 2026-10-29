using Gloaming.Agents.EntityFrameworkCore;
using Gloaming.Agents.Workflows.EntityFrameworkCore;
using Gloaming.Conversations.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AgentPersistenceDemo;

public sealed class DemoContext(DbContextOptions<DemoContext> options, string partition = "demo")
    : DbContext(options)
{
    public string Partition { get; } = partition;

    public static DemoContext Open(string connection, string partition = "demo") =>
        new(new DbContextOptionsBuilder<DemoContext>().UseNpgsql(connection).Options, partition);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureAgentHistory("documents");
        modelBuilder.ConfigureAgentCheckpoints("documents");
        modelBuilder.ConfigureConversationTranscript("documents");
        var head = modelBuilder.Entity<DocumentHead>();
        head.ToTable("document_heads", "documents");
        head.HasKey(x => x.Id);
        head.Property(x => x.Version).IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Guard();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        Guard();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Guard()
    {
        this.ValidateAgentHistoryChanges(Partition);
        this.ValidateAgentCheckpointsChanges(Partition);
        this.ValidateConversationTranscriptChanges(Partition);
    }
}

public sealed class DocumentHead
{
    public string Id { get; init; } = "document";
    public long Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string CheckpointId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Proposal { get; set; } = "";
    public string? Accepted { get; set; }
}
