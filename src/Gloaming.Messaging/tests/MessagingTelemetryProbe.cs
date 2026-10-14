using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.TestSupport;

// Native listeners assert mechanisms independently of an OTel SDK/exporter.
internal sealed class MessagingTelemetryProbe : IDisposable
{
    private readonly ActivityListener activities;
    private readonly MeterListener measurements = new();
    internal ConcurrentQueue<Activity> Spans { get; } = new();
    internal ConcurrentQueue<ActivityLink> SamplingLinks { get; } = new();
    internal ConcurrentQueue<(
        string Instrument,
        string Operation,
        string Result,
        double Value
    )> Metrics { get; } = new();

    internal MessagingTelemetryProbe(
        ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded
    )
    {
        activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Gloaming.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> creation) =>
            {
                if (creation.Links is not null)
                    foreach (var link in creation.Links)
                        SamplingLinks.Enqueue(link);

                return sampling;
            },
            ActivityStopped = Spans.Enqueue,
        };
        ActivitySource.AddActivityListener(activities);
        measurements.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Gloaming.Messaging")
                listener.EnableMeasurementEvents(instrument);
        };
        measurements.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Capture(instrument, value, tags)
        );
        measurements.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Capture(instrument, value, tags)
        );
        measurements.Start();
    }

    private void Capture(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    )
    {
        // The mechanism deliberately emits no message/tenant IDs as metric dimensions.
        Assert.Equal(2, tags.Length);
        string operation = "",
            result = "";
        foreach (var tag in tags)
        {
            if (tag.Key == "operation")
                operation = (string)tag.Value!;
            else if (tag.Key == "result")
                result = (string)tag.Value!;
            else
                Assert.Fail("Unexpected unbounded metric dimension: " + tag.Key);
        }

        Metrics.Enqueue((instrument.Name, operation, result, value));
    }

    public void Dispose()
    {
        activities.Dispose();
        measurements.Dispose();
    }
}

internal sealed class ContextLogger<T> : ILogger<T>
{
    internal ConcurrentQueue<(ActivityContext Context, Exception? Failure)> Errors { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (logLevel == LogLevel.Error)
            Errors.Enqueue((Activity.Current?.Context ?? default, exception));
    }
}
