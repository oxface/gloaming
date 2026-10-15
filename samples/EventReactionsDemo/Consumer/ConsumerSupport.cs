namespace ModulithFoundry.Samples.EventReactions;

public sealed record OrderState(Guid Id, decimal Amount, bool Submitted, long Version);

public sealed record AllowanceState(decimal Reserved, long Version, int Receipts, int Allocations);
