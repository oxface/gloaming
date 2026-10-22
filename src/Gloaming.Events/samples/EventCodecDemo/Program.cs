using ModulithFoundry.Samples.Wholesale.EventCodecDemo;

if (args.Contains("--schema-evolution", StringComparer.Ordinal))
    SchemaEvolutionJourneys.Run(Path.Combine(AppContext.BaseDirectory, "Fixtures"), Console.Out);
else
    DemoJourneys.Run(Path.Combine(AppContext.BaseDirectory, "Fixtures"), Console.Out);
