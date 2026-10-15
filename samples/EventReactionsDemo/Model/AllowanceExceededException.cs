namespace ModulithFoundry.Samples.EventReactions.Model;

public sealed class AllowanceExceededException(decimal ceiling, decimal requested)
    : Exception("The accepted orders would exceed this consumer's allowance.")
{
    public decimal Ceiling { get; } = ceiling;
    public decimal Requested { get; } = requested;
}
