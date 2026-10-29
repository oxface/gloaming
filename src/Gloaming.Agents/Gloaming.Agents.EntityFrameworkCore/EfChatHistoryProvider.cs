using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Gloaming.Agents.EntityFrameworkCore;

/// <summary>Native MAF history backed by immutable, checkpoint-referenced context snapshots.</summary>
public sealed class EfChatHistoryProvider<TContext> : ChatHistoryProvider
    where TContext : DbContext
{
    private readonly TContext database;
    private readonly string partitionKey;
    private readonly string conversationKey;
    private readonly string agentKey;
    private readonly IChatReducer? reducer;
    private readonly JsonSerializerOptions json;
    private readonly ProviderSessionState<HistoryState> state;

    /// <summary>Creates a scoped provider. Consumer keys grant no authority; writers must serialize conversation turns.</summary>
    public EfChatHistoryProvider(
        TContext database,
        string partitionKey,
        string conversationKey,
        string agentKey,
        IChatReducer? reducer = null,
        JsonSerializerOptions? serializerOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentKey);
        if (partitionKey.Length > 200 || conversationKey.Length > 200 || agentKey.Length > 200)
            throw new ArgumentException("Storage keys must not exceed 200 characters.");

        this.database = database;
        this.partitionKey = partitionKey;
        this.conversationKey = conversationKey;
        this.agentKey = agentKey;
        this.reducer = reducer;
        json = new(serializerOptions ?? AgentAbstractionsJsonUtilities.DefaultOptions);
        json.MakeReadOnly(populateMissingResolver: true);
        state = new(_ => new HistoryState(), "gloaming:history:" + agentKey);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => [state.StateKey];

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default
    ) => await ReadAsync(state.GetOrInitializeState(context.Session), cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("History writes require the caller's transaction.");

        var current = state.GetOrInitializeState(context.Session);
        var messages = await ReadAsync(current, cancellationToken);
        messages.AddRange(context.RequestMessages);
        messages.AddRange(context.ResponseMessages ?? []);
        if (reducer is not null)
            messages = [.. await reducer.ReduceAsync(messages, cancellationToken)];

        var id = Guid.NewGuid().ToString("N");
        database.Add(
            new AgentHistorySnapshot
            {
                PartitionKey = partitionKey,
                ConversationKey = conversationKey,
                AgentKey = agentKey,
                SessionKey = current.SessionKey,
                Id = id,
                ParentId = current.Head,
                Messages = JsonSerializer.Serialize(messages, json),
            }
        );
        await database.SaveChangesAsync(cancellationToken);
        current.Head = id;
        state.SaveState(context.Session, current);
    }

    private async Task<List<ChatMessage>> ReadAsync(
        HistoryState current,
        CancellationToken cancellationToken
    )
    {
        if (current.Head is null)
            return [];

        var payload =
            await database
                .Set<AgentHistorySnapshot>()
                .AsNoTracking()
                .Where(x =>
                    x.PartitionKey == partitionKey
                    && x.ConversationKey == conversationKey
                    && x.AgentKey == agentKey
                    && x.SessionKey == current.SessionKey
                    && x.Id == current.Head
                )
                .Select(x => x.Messages)
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(
                "The referenced history snapshot is unavailable in this scope."
            );
        return JsonSerializer.Deserialize<List<ChatMessage>>(payload, json)!;
    }

    /// <summary>Native session state stores an immutable snapshot reference, not a mutable latest-history query.</summary>
    public sealed class HistoryState
    {
        /// <summary>Identifies one native agent-session history lifetime.</summary>
        public string SessionKey { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>The exact selected snapshot, or null before the first successful invocation.</summary>
        public string? Head { get; set; }
    }
}
