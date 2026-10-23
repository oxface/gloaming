using Gloaming.Samples.Wholesale.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Samples.Wholesale.Api.HttpIntegration;

// The endpoint owns the returned read transaction; module persistence stays in this host bridge.
internal sealed class SalesReadTransaction(SalesDbContext database)
{
    internal Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken) =>
        database.Database.BeginTransactionAsync(cancellationToken);
}
