namespace Rootbolt.Messaging.Workflows;

/// <summary>Identity within a workflow type; optional tenant metadata must be admitted by the consumer.</summary>
public sealed record WorkflowKey(Guid Id, string? TenantKey = null);
