using Gloaming.EventReactions;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventReactions;

public sealed class MappedQuoteCommands(
    MappedQuotesContext database,
    IEventSink<MappedQuotesModule> events
)
{
    public async Task AcceptAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await database.Quotes.SingleAsync(row => row.Id == id, cancellationToken);
        var decision = row.ToDomain().Accept();
        row.Accepted = decision.State.Accepted;
        row.Version = decision.State.Version;
        events.Raise(decision.Fact);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await database.Quotes.SingleAsync(row => row.Id == id, cancellationToken);
        events.Raise(row.ToDomain().Delete());
        database.Quotes.Remove(row);
    }
}
