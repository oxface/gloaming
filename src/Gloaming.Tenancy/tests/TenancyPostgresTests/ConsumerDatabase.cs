using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gloaming.Tenancy.Postgres.Tests;

// Ordinary consumer model: no Sales, Access, actor, ownership utility or save capabilities.
internal sealed class ConsumerDatabase(NpgsqlDataSource dataSource)
    : DbContext(new DbContextOptionsBuilder().UseNpgsql(dataSource).Options)
{
    internal DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notes = modelBuilder.Entity<Note>();
        notes.ToTable("notes", "consumer");
        notes.HasKey(row => new { row.Partition, row.Id });
        notes.Property(row => row.Partition).HasColumnName("partition");
        notes.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        notes.Property(row => row.Body).HasColumnName("body");
    }
}

internal sealed class Note
{
    public string Partition { get; set; } = "";
    public int Id { get; set; }
    public string Body { get; set; } = "";
}
