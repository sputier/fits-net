```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 258V 2.20GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  MediumRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=MediumRun  IterationCount=15  LaunchCount=2  
WarmupCount=10  

```
| Method                 | Algorithm | Parallelism | Mean      | Error    | StdDev   | Median    | Rank | Gen0       | Gen1      | Gen2      | Allocated |
|----------------------- |---------- |------------ |----------:|---------:|---------:|----------:|-----:|-----------:|----------:|----------:|----------:|
| &#39;Decompress to ushort&#39; | Rice      | 1           |  72.73 ms | 1.357 ms | 2.031 ms |  71.76 ms |    1 |  3285.7143 | 2142.8571 |  857.1429 |  62.51 MB |
| Compress               | Rice      | 1           | 137.67 ms | 1.805 ms | 2.702 ms | 136.50 ms |    2 | 12250.0000 | 3500.0000 | 1250.0000 | 127.42 MB |
