using BenchmarkDotNet.Running;

// Run everything:                 dotnet run -c Release --project benchmarks/FitsNet.Benchmarks.Tables -- --filter "*"
// Quick, less accurate run:       add  --job short
// List the benchmarks:            add  --list flat
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
