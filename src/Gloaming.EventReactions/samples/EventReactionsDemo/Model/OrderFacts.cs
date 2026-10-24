namespace Gloaming.Samples.EventReactions.Model;

public sealed record OrderSubmitted(Guid Id, decimal Amount);

public sealed record AllowanceAllocated(Guid Id, decimal Amount);

public sealed record OrderDeleted(Guid Id, decimal Amount);
