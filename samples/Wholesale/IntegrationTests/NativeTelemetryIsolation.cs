namespace Gloaming.Samples.Wholesale.IntegrationTests;

// Native OTel sources/exporters are process-wide. No other class may publish while these proofs inspect them.
[CollectionDefinition("Native telemetry", DisableParallelization = true)]
public sealed class NativeTelemetryIsolation;
