using System.Runtime.CompilerServices;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.Workflows.EntityFrameworkCore;

/// <summary>Mandatory native SaveChanges integration for runner-owned workflow proposals.</summary>
public static class WorkflowSaveChangesExtensions
{
    /// <summary>Reject altered proposals and verify native stored values for unchanged process/request proposals; this validation can perform synchronous key reads.</summary>
    public static void ValidateWorkflowChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        foreach (var (entry, values, message) in UnchangedProposals(database))
            ValidateValues(entry.GetDatabaseValues(), values, message);
    }

    internal static async Task ValidateWorkflowChangesAsync(
        DbContext database,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var (entry, values, message) in UnchangedProposals(database))
            ValidateValues(await entry.GetDatabaseValuesAsync(cancellationToken), values, message);
    }

    private static IEnumerable<(
        EntityEntry Entry,
        Dictionary<string, object?> Values,
        string Message
    )> UnchangedProposals(DbContext database)
    {
        foreach (var proposal in ValidateTrackedChanges(database))
        {
            var process = database.Entry(proposal.Row);
            if (process.State == EntityState.Unchanged)
                yield return (
                    process,
                    proposal.Values,
                    "An unchanged workflow process must already be stored with its proposed values."
                );

            if (proposal.Command is not null)
            {
                var request = database.Entry(proposal.Command);
                if (request.State == EntityState.Unchanged)
                    yield return (
                        request,
                        proposal.CommandValues!,
                        "An unchanged workflow request must already be stored with its proposed values."
                    );
            }
        }
    }

    private static List<WorkflowProposal> ValidateTrackedChanges(DbContext database)
    {
        database.ChangeTracker.DetectChanges();
        var proposals = WorkflowChangeRegistry.For(database);
        foreach (var entry in database.ChangeTracker.Entries<WorkflowInstanceRecord>())
        {
            if (
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && !proposals.Any(proposal => ReferenceEquals(proposal.Row, entry.Entity))
            )
                throw new InvalidOperationException(
                    "Workflow rows must be changed through their bound runner."
                );
        }

        foreach (var proposal in proposals)
        {
            var entry = database.Entry(proposal.Row);
            if (
                !ReferenceEquals(database.Database.CurrentTransaction, proposal.Transaction)
                || entry.State is EntityState.Detached or EntityState.Deleted
            )
                throw new InvalidOperationException(
                    "A workflow proposal must remain in its originating native transaction."
                );

            ValidateValues(
                entry.CurrentValues,
                proposal.Values,
                "A workflow proposal was changed outside its runner."
            );

            if (
                entry.State == EntityState.Modified
                && (
                    entry.Property(row => row.Version).OriginalValue
                        != (long)proposal.Values[nameof(WorkflowInstanceRecord.Version)]! - 1
                    || !Equals(
                        entry.Property(row => row.TenantKey).OriginalValue,
                        proposal.Values[nameof(WorkflowInstanceRecord.TenantKey)]
                    )
                )
            )
                throw new InvalidOperationException(
                    "The observed workflow concurrency tokens must not be changed."
                );

            if (entry.State == EntityState.Modified)
                ValidateValues(
                    entry.OriginalValues,
                    proposal.OriginalValues,
                    "Preserve all observed workflow values so native column selection cannot omit a proposed turn."
                );

            if (proposal.Command is not null)
            {
                var request = database.Entry(proposal.Command);
                if (request.State is EntityState.Detached or EntityState.Deleted)
                    throw new InvalidOperationException(
                        "The outgoing workflow request must remain tracked with its process."
                    );

                ValidateValues(
                    request.CurrentValues,
                    proposal.CommandValues!,
                    "The outgoing workflow request was changed."
                );
            }
        }

        return proposals;
    }

    private static void ValidateValues(
        PropertyValues? actual,
        Dictionary<string, object?> expected,
        string message
    )
    {
        if (
            actual is null
            || expected.Any(property =>
                actual[property.Key] is JsonElement json && property.Value is JsonElement value
                    ? !JsonElement.DeepEquals(json, value)
                    : !Equals(actual[property.Key], property.Value)
            )
        )
            throw new InvalidOperationException(message);
    }
}

internal static class WorkflowChangeRegistry
{
    private static readonly ConditionalWeakTable<DbContext, WorkflowScope> Proposals = new();

    internal static List<WorkflowProposal> For(DbContext database)
    {
        var scope = Proposals.GetOrCreateValue(database);
        // A pooled context object may serve another DI lease; old proposals must not follow it.
        if (scope.ContextId != database.ContextId)
        {
            scope.ContextId = database.ContextId;
            scope.Proposals.Clear();
        }

        return scope.Proposals;
    }

    internal static void Register(
        DbContext database,
        WorkflowInstanceRecord row,
        OutboxMessageRecord? command,
        IDbContextTransaction transaction
    )
    {
        var values = database
            .Entry(row)
            .Properties.ToDictionary(
                property => property.Metadata.Name,
                property => property.CurrentValue
            );
        // Ignore database-generated dispatch fields; the immutable wire envelope is the obligation here.
        var commandValues = command is null
            ? null
            : database
                .Entry(command)
                .Properties.Where(property =>
                    property.Metadata.ValueGenerated == ValueGenerated.Never
                )
                .ToDictionary(
                    property => property.Metadata.Name,
                    property => property.CurrentValue
                );
        var originalValues = database
            .Entry(row)
            .Properties.ToDictionary(
                property => property.Metadata.Name,
                property => property.OriginalValue
            );
        For(database).Add(new(row, command, transaction, values, originalValues, commandValues));
    }
}

internal sealed class WorkflowScope
{
    internal DbContextId ContextId { get; set; }
    internal List<WorkflowProposal> Proposals { get; } = [];
}

internal sealed record WorkflowProposal(
    WorkflowInstanceRecord Row,
    OutboxMessageRecord? Command,
    IDbContextTransaction Transaction,
    Dictionary<string, object?> Values,
    Dictionary<string, object?> OriginalValues,
    Dictionary<string, object?>? CommandValues
);
