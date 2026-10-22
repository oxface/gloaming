using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Dispatch;

internal sealed class CustomerDirectory(DispatchDbContext database, ITenantContextAccessor tenancy)
    : ICustomerDirectory
{
    private string Organization
    {
        get
        {
            string organization = tenancy.Current.RequireTenant().Value;
            if (organization is not ("wholesale-alpha" or "wholesale-beta"))
                throw new InvalidDataException("Dispatch has not admitted this Organization.");

            return organization;
        }
    }

    public async Task<DirectoryProgress> BeginAsync(CancellationToken token)
    {
        string organization = Organization;
        Guid id = Guid.NewGuid();
        // Durable capture registration precedes every source read. Retries preserve the generation/cursor.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dispatch.directory_populations (\"Organization\", \"PopulationId\", \"Ready\", \"Copied\") VALUES ({organization}, {id}, false, 0) ON CONFLICT DO NOTHING",
            token
        );
        return await ProgressAsync(token);
    }

    public async Task<DirectoryProgress> ProgressAsync(CancellationToken token) =>
        Progress(
            await database
                .Populations.AsNoTracking()
                .SingleAsync(row => row.Organization == Organization, token)
        );

    public async Task ApplyPageAsync(
        Guid populationId,
        Guid? expectedAfter,
        CustomerDeliveryPage page,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(page);
        if (
            page.Organization != Organization
            || page.Entries.Count > 500
            || (page.Entries.Count == 0 && !page.Complete)
            || page.Next != (page.Entries.Count == 0 ? expectedAfter : page.Entries[^1].CustomerId)
        )
            throw new InvalidDataException("Invalid directory export page.");

        Guid? previous = expectedAfter;
        foreach (var entry in page.Entries)
        {
            if (previous.HasValue && entry.CustomerId.CompareTo(previous.Value) <= 0)
                throw new InvalidDataException("Directory export keys must increase.");

            previous = entry.CustomerId;
        }

        string organization = Organization;
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var population = await DirectoryMerge.LockAsync(database, organization, token);
        if (
            population.PopulationId != populationId
            || population.After != expectedAfter
            || population.Ready
        )
            throw new InvalidOperationException("The population page belongs to stale progress.");

        foreach (var entry in page.Entries)
            await DirectoryMerge.ApplyAsync(database, organization, entry, token);

        population.After = page.Next;
        population.Copied = checked(population.Copied + page.Entries.Count);
        population.Ready = page.Complete;
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<IReadOnlyList<DispatchCustomer>> SearchAsync(
        string text,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        string organization = Organization;
        if (!(await ProgressAsync(token)).Ready)
            throw new InvalidOperationException("The Dispatch directory is still populating.");

        return await database
            .Customers.AsNoTracking()
            .Where(row =>
                row.Organization == organization
                && row.Listed
                && (
                    row.Code!.Contains(text)
                    || row.DisplayName!.Contains(text)
                    || row.AddressLine!.Contains(text)
                )
            )
            .OrderBy(row => row.Code)
            .Take(100)
            .Select(row => new DispatchCustomer(
                row.CustomerId,
                row.Code!,
                row.DisplayName!,
                row.AddressLine!
            ))
            .ToArrayAsync(token);
    }

    private static DirectoryProgress Progress(PopulationRow row) =>
        new(row.PopulationId, row.After, row.Ready, row.Copied);
}
