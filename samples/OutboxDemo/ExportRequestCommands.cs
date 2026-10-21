using System.Diagnostics;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.OutboxDemo;

public sealed class ExportRequest
{
    private ExportRequest() { }

    public Guid Id { get; private set; }
    public int Pages { get; private set; }
    public long Version { get; private set; }
    public bool Submitted { get; private set; }

    public static ExportRequest Create(Guid id, int pages)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(pages);
        return new()
        {
            Id = id,
            Pages = pages,
            Version = 1,
        };
    }

    internal bool TrySubmit()
    {
        if (Submitted || Pages == 0)
            return false;
        Submitted = true;
        Version++;
        return true;
    }
}

public enum ExportSubmissionResult
{
    Accepted = 1,
    NotFound = 2,
    Conflict = 3,
    Ineligible = 4,
}

public sealed record RenderExportV1(Guid ExportRequestId, int Pages);

public sealed record RenderExportV2(Guid ExportRequestId, int PageCount);

/// <summary>Sample application command handler: decides submission from tracked state and enqueues accepted work.</summary>
/// <remarks>The caller supplies the native transaction and performs the final SaveChanges/commit.</remarks>
public sealed class ExportRequestCommands(
    ExportDbContext database,
    IOutbox<ExportDbContext> outbox,
    int renderSchemaVersion = 1
)
{
    private static readonly JsonSerializerOptions WireJson = new();
    private readonly int schemaVersion = renderSchemaVersion is 1 or 2
        ? renderSchemaVersion
        : throw new ArgumentOutOfRangeException(
            nameof(renderSchemaVersion),
            "Select render schema 1 or 2."
        );

    public async Task<ExportSubmissionResult> SubmitAsync(
        Guid id,
        long expectedVersion,
        CancellationToken cancellationToken
    )
    {
        var request = await database
            .Set<ExportRequest>()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (request is null)
            return ExportSubmissionResult.NotFound;
        if (request.Version != expectedVersion)
            return ExportSubmissionResult.Conflict;
        if (!request.TrySubmit())
            return ExportSubmissionResult.Ineligible;
        // A directed integration command, using ordinary JSON without an event codec or aggregate store.
        outbox.Enqueue(
            OutgoingMessage.FromPayload<object>(
                Guid.NewGuid(),
                "exports.render",
                "exports.render",
                schemaVersion,
                schemaVersion == 1
                    ? new RenderExportV1(request.Id, request.Pages)
                    : new RenderExportV2(request.Id, request.Pages),
                WireJson,
                traceParent: Activity.Current?.Id,
                traceState: Activity.Current?.TraceStateString
            )
        );
        return ExportSubmissionResult.Accepted;
    }
}
