using Gloaming.Messaging.EntityFrameworkCore;
using Npgsql;

namespace ModulithFoundry.Samples.InboxDemo;

// Rendering owns this classification, budget, delay and safe reason vocabulary.
public sealed class RenderInboxFailurePolicy : IInboxFailurePolicy<RenderDbContext>
{
    public InboxFailureDecision Decide(InboxFailure failure)
    {
        if (failure.Exception is InvalidDataException or System.Text.Json.JsonException)
            return new InboxFailureDecision.DeadLetter("render-command-conflict");

        Exception error = failure.Exception;
        while (error.InnerException is { } inner)
            error = inner;

        if (
            error is PostgresException
            {
                SqlState: PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected
            }
        )
            return failure.FailuresSinceRedrive < 3
                ? new InboxFailureDecision.RetryAfter(TimeSpan.FromSeconds(1), "render-transient")
                : new InboxFailureDecision.DeadLetter("render-retry-limit");

        return new InboxFailureDecision.DeadLetter("render-review-required");
    }
}
