using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;

// The endpoint owns the returned read transaction; module persistence stays in this host bridge.
internal sealed class SalesReadTransaction(SalesDbContext database)
{
    internal Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken) =>
        database.Database.BeginTransactionAsync(cancellationToken);
}
