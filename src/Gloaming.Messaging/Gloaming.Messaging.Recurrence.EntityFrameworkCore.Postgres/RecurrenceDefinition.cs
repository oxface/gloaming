using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cronos;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

/// <summary>The explicitly selected treatment of unplanned occurrences at or before a planning cutoff.</summary>
public sealed class MissedOccurrences
{
    internal int Kind { get; }
    internal int Maximum { get; }

    private MissedOccurrences(int kind, int maximum) => (Kind, Maximum) = (kind, maximum);

    /// <summary>Omit unplanned elapsed occurrences. Already staged finite work is retained.</summary>
    public static MissedOccurrences Skip { get; } = new(1, 1);

    /// <summary>Emit only the latest unplanned occurrence through a captured cutoff, after bounded forward scanning completes.</summary>
    public static MissedOccurrences Coalesce { get; } = new(2, 2);

    /// <summary>Emit earliest unplanned elapsed occurrences in batches of at most 1..100, including any newly armed future occurrence.</summary>
    public static MissedOccurrences CatchUp(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 100);

        return new(3, maximum);
    }
}

/// <summary>Explicit supported cron syntax. The numeric assignments are protocol values.</summary>
public enum RecurrenceCronFormat
{
    /// <summary>Minute, hour, day, month, day of week.</summary>
    FiveFields = 1,

    /// <summary>Second, minute, hour, day, month, day of week.</summary>
    SixFields = 2,
}

/// <summary>An immutable calendar definition. Changing its version, rules, runtime or evaluator requires a new retained generation.</summary>
public sealed class RecurrenceDefinition
{
    private readonly CronExpression? cron;
    private readonly TimeZoneInfo? zone;
    private readonly long intervalTicks;
    internal DateTimeOffset Anchor { get; }
    internal MissedOccurrences Missed { get; }
    internal string Descriptor { get; }

    private RecurrenceDefinition(
        string version,
        DateTimeOffset anchor,
        MissedOccurrences missed,
        TimeSpan? interval,
        string? expression,
        RecurrenceCronFormat? format,
        TimeZoneInfo? zone,
        string? rulesVersion
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(missed);
        RequireInstant(anchor);

        Anchor = anchor;
        Missed = missed;
        this.zone = zone;
        intervalTicks = interval?.Ticks ?? 0;
        if (expression is not null)
            cron = CronExpression.Parse(
                expression,
                format == RecurrenceCronFormat.FiveFields
                    ? CronFormat.Standard
                    : CronFormat.IncludeSeconds
            );

        Descriptor = JsonSerializer.Serialize(
            new
            {
                Protocol = 1,
                Version = version,
                Anchor = anchor,
                IntervalTicks = intervalTicks,
                Expression = expression,
                Format = format,
                Zone = zone?.Id,
                RulesVersion = rulesVersion,
                // A conservative mismatch detector for the actual immutable object used below;
                // not a portable zone snapshot or an attestation of the host's tzdata source.
                ZoneFingerprint = zone is null ? null : Hash(zone.ToSerializedString()),
                Runtime = RuntimeInformation.FrameworkDescription,
                Evaluator = typeof(CronExpression)
                    .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion,
                MissedPolicy = missed.Kind,
                CatchUpMaximum = missed.Maximum,
            }
        );
    }

    /// <summary>Uses elapsed UTC intervals strictly after the caller's anchor; no local calendar is inferred.</summary>
    public static RecurrenceDefinition Every(
        string version,
        DateTimeOffset anchor,
        TimeSpan interval,
        MissedOccurrences missed
    )
    {
        if (interval <= TimeSpan.Zero || interval.Ticks % 10 != 0)
            throw new ArgumentException(
                "Use a positive interval at microsecond precision.",
                nameof(interval)
            );

        return new(version, anchor, missed, interval, null, null, null, null);
    }

    /// <summary>Uses forward Cronos evaluation with an explicit zone, caller-attested rules version, syntax and anchor.</summary>
    /// <remarks>The consumer pins and verifies its actual time-zone data source. The serialized-zone fingerprint is a conservative mismatch check, not portable rule restoration.</remarks>
    public static RecurrenceDefinition Cron(
        string version,
        DateTimeOffset anchor,
        string expression,
        RecurrenceCronFormat format,
        TimeZoneInfo zone,
        string rulesVersion,
        MissedOccurrences missed
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesVersion);
        ArgumentNullException.ThrowIfNull(zone);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));

        return new(version, anchor, missed, null, expression, format, zone, rulesVersion);
    }

    internal DateTimeOffset? Next(DateTimeOffset after)
    {
        if (cron is not null)
            return cron.GetNextOccurrence(after, zone!, inclusive: false)?.ToUniversalTime();

        long elapsed = after.Ticks - Anchor.Ticks;
        long steps = elapsed / intervalTicks + 1;
        if (steps > (DateTimeOffset.MaxValue.Ticks - Anchor.Ticks) / intervalTicks)
            return null;

        return new DateTimeOffset(Anchor.Ticks + steps * intervalTicks, TimeSpan.Zero);
    }

    internal static void RequireInstant(DateTimeOffset instant)
    {
        if (instant.Offset != TimeSpan.Zero || instant.Ticks % 10 != 0)
            throw new ArgumentException("Use an explicit UTC instant at microsecond precision.");
    }

    internal static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
