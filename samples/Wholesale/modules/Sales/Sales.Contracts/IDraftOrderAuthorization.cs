namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

// Actor and Organization come from the trusted, immutable operation context.
// This check is also enforced by the draft queries and existing-resource commands.
public interface IDraftOrderAuthorization
{
    Task<bool> IsAllowedAsync(
        Guid id,
        DraftOrderAction permission,
        CancellationToken cancellationToken
    );
}

public enum DraftOrderAction
{
    Read = 1,
    Edit = 2,
}
