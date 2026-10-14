namespace OpenTelemetry.Metrics;

/// <summary>Opt-in collection of Gloaming messaging measurements by a native OTel provider.</summary>
public static class GloamingMessagingMeterProviderBuilderExtensions
{
    /// <summary>Subscribes to the Gloaming.Messaging meter.</summary>
    /// <param name="builder">The host's native meter provider builder.</param>
    /// <returns>The supplied builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException">The builder is null.</exception>
    /// <remarks>
    /// Resources, aggregation and exporters remain host-owned.
    /// This does not enable traces or configure logging.
    /// </remarks>
    public static MeterProviderBuilder AddGloamingMessaging(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddMeter("Gloaming.Messaging");
    }
}
