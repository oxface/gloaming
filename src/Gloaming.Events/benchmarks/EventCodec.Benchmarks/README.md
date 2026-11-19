# Event codec benchmarks

Use the pinned .NET SDK to measure decoding a current event and the one-step
and two-step upcast paths to the same current event. The benchmarks share the
[literal fixtures](../../samples/EventCodecDemo/Fixtures/README.md) and consumer
upcasters used by the [compatibility tests](../../tests/CodecAdoptionTests/UpcastingCompatibilityTests.cs).
BenchmarkDotNet measures codec calls against pre-parsed envelopes, with
file reads, envelope parsing and codec setup outside measurement.

Run the selected target from the repository root.

```sh
npm run perf:events
```

From `src/Gloaming.Events`, use `npm run perf`.
BenchmarkDotNet writes native HTML, JSON, CSV and Markdown under `results/` in
`event-codec/` in the printed run directory. Compare time and bytes allocated per
decode on the same host and runtime. For BenchmarkDotNet-specific arguments,
invoke the console project directly with `dotnet run -c Release -- --help`
from its project folder.
