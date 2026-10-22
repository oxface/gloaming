using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;

public sealed record DirectoryProgress(Guid PopulationId, Guid? After, bool Ready, long Copied);

public sealed record DispatchCustomer(
    Guid CustomerId,
    string Code,
    string DisplayName,
    string AddressLine
);

// These are trusted module boundaries. Transport authenticates Sales; payload metadata is not a credential.
public interface ICustomerDirectoryIntake
{
    Task ReceiveAsync(
        Guid messageId,
        string organization,
        CustomerDeliveryState state,
        CancellationToken token
    );
}

public interface ICustomerDirectory
{
    Task<DirectoryProgress> BeginAsync(CancellationToken token);
    Task<DirectoryProgress> ProgressAsync(CancellationToken token);
    Task ApplyPageAsync(
        Guid populationId,
        Guid? expectedAfter,
        CustomerDeliveryPage page,
        CancellationToken token
    );
    Task<IReadOnlyList<DispatchCustomer>> SearchAsync(string text, CancellationToken token);
}
