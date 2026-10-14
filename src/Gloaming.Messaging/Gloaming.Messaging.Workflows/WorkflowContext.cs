namespace Gloaming.Messaging.Workflows;

/// <summary>Execution metadata for one uncommitted turn. Returned requests must retain CommandId.</summary>
public sealed record WorkflowContext(WorkflowKey Key, Guid CommandId, Guid? CauseMessageId);
