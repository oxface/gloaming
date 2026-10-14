using System.Text.Json;
using Gloaming.Messaging.Workflows;

namespace Gloaming.Messaging.Workflows.EntityFrameworkCore;

/// <summary>Provided process row. Only the bound runner may propose tracked updates.</summary>
public sealed class WorkflowInstanceRecord
{
    private WorkflowInstanceRecord() { }

    /// <summary>Stable definition identity, part of the primary key.</summary>
    public string WorkflowType { get; private set; } = null!;

    /// <summary>Instance identity, unique within its workflow type across tenants.</summary>
    public Guid Id { get; private set; }

    /// <summary>Optional admitted tenant metadata, checked on every lookup and concurrency update.</summary>
    public string? TenantKey { get; private set; }

    /// <summary>Version of the consumer definition that created this process.</summary>
    public int DefinitionVersion { get; private set; }

    /// <summary>Optimistic concurrency token; advances once per accepted turn.</summary>
    public long Version { get; private set; }

    /// <summary>Technical lifecycle, separate from the business state.</summary>
    public WorkflowStatus Status { get; private set; }

    /// <summary>Named pending reply or signal binding; null after completion.</summary>
    public string? ContinuationName { get; private set; }

    /// <summary>Requested outgoing operation identity; present only while waiting.</summary>
    public Guid? PendingCommandId { get; private set; }

    /// <summary>Retained initial input for equal-start detection.</summary>
    public JsonElement InitialInput { get; private set; }

    /// <summary>Serialized consumer state, restored to a detached candidate before execution.</summary>
    public JsonElement State { get; private set; }

    /// <summary>Retained requested operations and semantic outcomes for duplicate/conflict matching.</summary>
    public JsonElement Operations { get; private set; }

    internal static WorkflowInstanceRecord Create(
        string workflowType,
        WorkflowKey key,
        int definitionVersion,
        JsonElement initialInput
    ) =>
        new()
        {
            WorkflowType = workflowType,
            Id = key.Id,
            TenantKey = key.TenantKey,
            DefinitionVersion = definitionVersion,
            InitialInput = initialInput,
        };

    internal void Advance(
        WorkflowStatus status,
        string? continuationName,
        Guid? pendingCommandId,
        JsonElement state,
        JsonElement operations
    )
    {
        Version = checked(Version + 1);
        Status = status;
        ContinuationName = continuationName;
        PendingCommandId = pendingCommandId;
        State = state;
        Operations = operations;
    }
}
