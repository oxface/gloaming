using System.Net;
using Gloaming.Messaging.EntityFrameworkCore;

namespace ModulithFoundry.Samples.OutboxDemo;

// HTTP classification, budget and safe reason vocabulary belong to this consumer.
public sealed class ExportOutboxFailurePolicy : IOutboxFailurePolicy<ExportDbContext>
{
    public OutboxFailureDecision Decide(OutboxFailure failure)
    {
        if (failure.Exception is InvalidDataException)
            return new OutboxFailureDecision.DeadLetter("export-contract-unsupported");

        if (
            failure.Exception is TimeoutException or TaskCanceledException
            || failure.Exception
                is HttpRequestException
                {
                    StatusCode: null
                        or HttpStatusCode.RequestTimeout
                        or HttpStatusCode.TooManyRequests
                }
            || failure.Exception is HttpRequestException { StatusCode: { } status }
                && (int)status >= 500
        )
            return failure.FailuresSinceRedrive < 3
                ? new OutboxFailureDecision.RetryAfter(
                    TimeSpan.FromSeconds(1),
                    "export-transport-transient"
                )
                : new OutboxFailureDecision.DeadLetter("export-retry-limit");

        return new OutboxFailureDecision.DeadLetter("export-review-required");
    }
}
