using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gloaming.Messaging;

// Native instrumentation shared by callable operations, independently of hosted workers/exporters.
internal static class MessagingTelemetry
{
    private static readonly ActivitySource Activities = new("Gloaming.Messaging");
    private static readonly Meter Measurements = new("Gloaming.Messaging");
    private static readonly Counter<long> Attempts = Measurements.CreateCounter<long>(
        "gloaming.messaging.attempts",
        "{attempt}"
    );
    private static readonly Histogram<double> Duration = Measurements.CreateHistogram<double>(
        "gloaming.messaging.attempt.duration",
        "s"
    );

    internal sealed class Attempt : IDisposable
    {
        private readonly Activity? activity;
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly string operation;
        private string result = "failed";

        internal Attempt(
            string operation,
            Guid messageId,
            string? correlationId,
            string? causationId,
            string? traceParent,
            string? traceState
        )
        {
            this.operation = operation;

            // Supply links at creation so samplers can inspect the retained relationship.
            ActivityLink[]? links = ActivityContext.TryParse(
                traceParent,
                traceState,
                isRemote: true,
                out var upstream
            )
                ? [new(upstream)]
                : null;
            activity = Activities.StartActivity(
                operation == "dispatch" ? "gloaming.outbox.dispatch" : "gloaming.inbox.process",
                operation == "dispatch" ? ActivityKind.Internal : ActivityKind.Consumer,
                Activity.Current?.Context ?? default,
                new ActivityTagsCollection
                {
                    ["messaging.message.id"] = messageId.ToString(),
                    ["gloaming.messaging.correlation_id"] = correlationId,
                    ["gloaming.messaging.causation_id"] = causationId,
                },
                links
            );
        }

        internal void Complete(string outcome)
        {
            result = outcome;
            activity?.SetStatus(
                outcome == "claim_lost" ? ActivityStatusCode.Error : ActivityStatusCode.Ok
            );
        }

        internal void Cancel() => result = "cancelled";

        internal void Fail(Exception failure)
        {
            result = "failed";
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", failure.GetType().FullName);
        }

        public void Dispose()
        {
            activity?.SetTag("gloaming.messaging.result", result);
            // Measurement still occurs when tracing has no listener or is sampled out.
            var tags = new TagList { { "operation", operation }, { "result", result } };
            Attempts.Add(1, tags);
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
            activity?.Dispose();
        }
    }
}
