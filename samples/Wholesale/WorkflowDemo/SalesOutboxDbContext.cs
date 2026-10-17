using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo;

// Delivery scans cross tenant boundaries. This context has no business mappings or tenant accessor;
// its separate database login limits it to reading envelopes and updating delivery state.
public sealed class SalesOutboxDbContext(DbContextOptions<SalesOutboxDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ConfigurePostgresOutbox("sales", "outbox_messages");
}
