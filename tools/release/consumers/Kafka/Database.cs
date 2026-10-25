using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

// Consumer-owned fixture mappings; no application business policy is prescribed.
sealed class SenderDatabase(DbContextOptions<SenderDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigurePostgresOutbox("sender", "outbox");
        model.Entity<RequestRow>().ToTable("requests", "sender");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateOutboxChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken token = default
    )
    {
        this.ValidateOutboxChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, token);
    }
}

sealed class ReceiverDatabase(DbContextOptions<ReceiverDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigurePostgresInbox("receiver", "inbox");
        model.Entity<EffectRow>().ToTable("effects", "receiver");
        model.Entity<QuarantineRow>().ToTable("quarantine", "receiver");
        model.Entity<QuarantineRow>().Property(row => row.HeadersJson).HasColumnType("jsonb");
        model
            .Entity<QuarantineRow>()
            .HasKey(row => new
            {
                row.Source,
                row.Topic,
                row.Partition,
                row.Offset,
            });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateInboxChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken token = default
    )
    {
        this.ValidateInboxChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, token);
    }
}

sealed class RequestRow
{
    public Guid Id { get; set; }
    public int Amount { get; set; }
}

sealed class EffectRow
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public int Amount { get; set; }
}

sealed class QuarantineRow
{
    // Consumer-selected cluster/topic incarnation; positions alone are not permanent source identity.
    public string Source { get; set; } = "";
    public string Topic { get; set; } = "";
    public int Partition { get; set; }
    public long Offset { get; set; }
    public byte[]? Raw { get; set; }
    public byte[]? RecordKey { get; set; }
    public string HeadersJson { get; set; } = "[]";
    public long Timestamp { get; set; }
    public string Reason { get; set; } = "";
}

sealed class FailureSwitch
{
    internal bool FailAfterSave { get; set; }
}

sealed class EffectHandler(ReceiverDatabase database, FailureSwitch failure)
    : IInboxHandler<ReceiverDatabase>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken token)
    {
        database.Add(
            new EffectRow
            {
                Id = message.Payload.GetProperty("item").GetGuid(),
                MessageId = message.MessageId,
                Amount = message.Payload.GetProperty("amount").GetInt32(),
            }
        );
        await database.SaveChangesAsync(token);
        if (failure.FailAfterSave)
            throw new InjectedHandlerFailure();
    }
}

sealed class InjectedHandlerFailure : Exception;

sealed record WireEnvelope(
    Guid MessageId,
    string MessageName,
    int SchemaVersion,
    JsonElement Payload,
    string? TenantKey,
    string? CorrelationId,
    string? CausationId,
    string? TraceParent = null,
    string? TraceState = null
);
