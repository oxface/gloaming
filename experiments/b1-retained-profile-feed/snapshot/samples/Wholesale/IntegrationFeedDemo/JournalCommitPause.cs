using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo;

// Opt-in finite diagnostic mode for abrupt process-death proofs; never selected by normal reads.
internal sealed class JournalCommitPause : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine("Journal saved before commit.");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return result;
    }
}
