namespace Gloaming.Samples.Wholesale.Sales.Contracts;

/// <summary>The operation identity already belongs to different input in this Organization and operation.</summary>
public sealed class CommandReceiptConflictException : InvalidOperationException
{
    public CommandReceiptConflictException()
        : base("The command operation identity was already used for different input.") { }
}
