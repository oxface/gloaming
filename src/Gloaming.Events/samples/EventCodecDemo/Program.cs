using Gloaming.Samples.Wholesale.EventCodecDemo;

if (args.Length != 0)
    throw new ArgumentException("This example takes no arguments.");

CodecExample.Run(Path.Combine(AppContext.BaseDirectory, "Fixtures"), Console.Out);
