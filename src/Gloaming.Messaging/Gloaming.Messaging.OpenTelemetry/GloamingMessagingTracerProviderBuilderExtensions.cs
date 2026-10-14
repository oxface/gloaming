namespace OpenTelemetry.Trace;

/// <summary>Opt-in collection of Gloaming messaging activities by a native OTel provider.</summary>
public static class GloamingMessagingTracerProviderBuilderExtensions
{
    /// <summary>Subscribes to the Gloaming.Messaging activity source.</summary>
    /// <param name="builder">The host's native tracer provider builder.</param>
    /// <returns>The supplied builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException">The builder is null.</exception>
    /// <remarks>
    /// Sampling, resources, exporters and transport instrumentation remain host-owned.
    /// This does not enable metrics or configure logging.
    /// </remarks>
    public static TracerProviderBuilder AddGloamingMessaging(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddSource("Gloaming.Messaging");
    }
}
