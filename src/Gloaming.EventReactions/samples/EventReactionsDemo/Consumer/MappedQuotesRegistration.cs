using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.EventReactions;

public static class MappedQuotesRegistration
{
    public static IServiceCollection AddMappedQuotesModule(
        this IServiceCollection services,
        string connection,
        Action<
            EventReactionRegistration<MappedQuotesContext, MappedQuotesModule>
        >? configureEvents = null
    )
    {
        services.AddEventReactions<MappedQuotesContext, MappedQuotesModule>(events =>
        {
            events.AddReaction<QuoteAccepted, ReserveQuoteAllowance>();
            events.AddReaction<QuoteAccepted, RecordQuoteReceipt>();
            events.AddReaction<QuoteAllowanceAllocated, RecordQuoteAllocation>();
            events.AddReaction<QuoteDeleted, ReleaseQuoteAllowance>();
            configureEvents?.Invoke(events);
        });
        services.AddScoped<MappedQuoteCommands>();
        return services.AddDbContext<MappedQuotesContext>(
            (provider, options) =>
                options
                    .ConfigureGloamingCapabilities<MappedQuotesContext>(provider)
                    .UseNpgsql(
                        connection,
                        postgres =>
                            postgres.MigrationsHistoryTable(
                                "__EFMigrationsHistory",
                                "mapped_quotes"
                            )
                    )
        );
    }
}
