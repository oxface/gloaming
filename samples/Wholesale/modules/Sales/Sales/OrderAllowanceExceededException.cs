namespace Gloaming.Samples.Wholesale.Sales;

// A required local reaction rejected the proposal; roll back and dispose the operation scope.
public sealed class OrderAllowanceExceededException(
    Guid customerId,
    decimal ceiling,
    decimal requested
) : Exception("Submitting this draft would exceed the customer's order allowance.")
{
    public Guid CustomerId { get; } = customerId;
    public decimal Ceiling { get; } = ceiling;
    public decimal Requested { get; } = requested;
}
