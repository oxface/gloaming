using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Dispatch;

internal static class DirectoryMerge
{
    internal static void Validate(string organization, CustomerDeliveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        DirectoryAdmission.Require(organization);

        if (
            state.CustomerId == Guid.Empty
            || state.Revision < 1
            || (
                state.Listed
                && (
                    string.IsNullOrWhiteSpace(state.Code)
                    || state.Code.Length > 128
                    || string.IsNullOrWhiteSpace(state.DisplayName)
                    || state.DisplayName.Length > 256
                    || string.IsNullOrWhiteSpace(state.AddressLine)
                    || state.AddressLine.Length > 512
                )
            )
            || (
                !state.Listed
                && (
                    state.Code is not null
                    || state.DisplayName is not null
                    || state.AddressLine is not null
                )
            )
        )
            throw new InvalidDataException("The Sales public directory state is invalid.");
    }

    // The caller holds this Organization's population row lock in its transaction.
    internal static async Task ApplyAsync(
        DispatchDbContext database,
        string organization,
        CustomerDeliveryState state,
        CancellationToken token
    )
    {
        Validate(organization, state);

        var row = await database.Customers.SingleOrDefaultAsync(
            row => row.Organization == organization && row.CustomerId == state.CustomerId,
            token
        );
        if (row is not null && row.Revision > state.Revision)
            return;

        if (row is not null && row.Revision == state.Revision)
        {
            if (
                row.Listed != state.Listed
                || row.Code != state.Code
                || row.DisplayName != state.DisplayName
                || row.AddressLine != state.AddressLine
            )
                throw new InvalidDataException("Equal public revisions contain different state.");

            return;
        }

        if (row is null)
        {
            row = new DirectoryRow { Organization = organization, CustomerId = state.CustomerId };
            database.Customers.Add(row);
        }

        row.Revision = state.Revision;
        row.Listed = state.Listed;
        row.Code = state.Code;
        row.DisplayName = state.DisplayName;
        row.AddressLine = state.AddressLine;
    }

    internal static Task<PopulationRow> LockAsync(
        DispatchDbContext database,
        string organization,
        CancellationToken token
    ) =>
        database
            .Populations.FromSqlInterpolated(
                $"SELECT * FROM dispatch.directory_populations WHERE \"Organization\" = {organization} FOR UPDATE"
            )
            .SingleAsync(token);
}
