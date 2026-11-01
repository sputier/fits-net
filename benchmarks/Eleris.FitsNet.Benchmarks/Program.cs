using BenchmarkDotNet.Running;

// Run everything:                 dotnet run -c Release --project benchmarks/Eleris.FitsNet.Benchmarks -- --filter "*"
// Pick a class:                   dotnet run -c Release --project benchmarks/Eleris.FitsNet.Benchmarks -- --filter "*CompressionBenchmarks*"
// Quick, less accurate run:       add  --job short
// List the benchmarks:            add  --list flat
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
