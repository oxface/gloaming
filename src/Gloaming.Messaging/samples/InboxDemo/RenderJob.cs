namespace ModulithFoundry.Samples.InboxDemo;

// Recording a job is local durable work, not claiming that external rendering completed.
public sealed class RenderJob
{
    private RenderJob() { }

    public Guid ExportRequestId { get; private set; }
    public int Pages { get; private set; }

    public static RenderJob Create(Guid exportRequestId, int pages)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(exportRequestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages);
        return new() { ExportRequestId = exportRequestId, Pages = pages };
    }
}
