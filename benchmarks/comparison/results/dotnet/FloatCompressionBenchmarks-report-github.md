```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 258V 2.20GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  MediumRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=MediumRun  IterationCount=15  LaunchCount=2  
WarmupCount=10  

```
| Method               | Quantization       | Parallelism | Mean      | Error    | StdDev    | Median    | Rank | Gen0       | Gen1      | Gen2      | Allocated |
|--------------------- |------------------- |------------ |----------:|---------:|----------:|----------:|-----:|-----------:|----------:|----------:|----------:|
| &#39;Decompress float32&#39; | NoDither           | -1          |  24.15 ms | 0.427 ms |  0.639 ms |  24.01 ms |    1 |  2156.2500 | 1750.0000 |  562.5000 | 105.06 MB |
| &#39;Decompress float32&#39; | SubtractiveDither2 | -1          |  33.09 ms | 2.285 ms |  3.420 ms |  33.07 ms |    2 |  2125.0000 | 1750.0000 |  562.5000 | 105.06 MB |
| &#39;Decompress float32&#39; | SubtractiveDither1 | -1          |  37.25 ms | 3.120 ms |  4.670 ms |  35.05 ms |    2 |  2142.8571 | 1714.2857 |  571.4286 | 105.06 MB |
| &#39;Decompress float32&#39; | None               | -1          |  45.60 ms | 2.186 ms |  3.204 ms |  45.73 ms |    3 |  3416.6667 | 2750.0000 | 1166.6667 | 204.56 MB |
| &#39;Compress float32&#39;   | NoDither           | -1          |  73.80 ms | 3.271 ms |  4.896 ms |  75.49 ms |    4 |  4285.7143 | 2142.8571 |  857.1429 | 132.38 MB |
| &#39;Compress float32&#39;   | SubtractiveDither2 | -1          |  74.75 ms | 2.653 ms |  3.971 ms |  74.96 ms |    4 |  4285.7143 | 2142.8571 |  857.1429 | 132.37 MB |
| &#39;Compress float32&#39;   | SubtractiveDither1 | -1          |  83.85 ms | 1.888 ms |  2.826 ms |  84.04 ms |    5 |  4166.6667 | 2000.0000 |  833.3333 | 132.37 MB |
| &#39;Decompress float32&#39; | NoDither           | 1           |  87.39 ms | 3.223 ms |  4.824 ms |  87.22 ms |    5 |  2000.0000 | 1666.6667 |  500.0000 | 105.05 MB |
| &#39;Decompress float32&#39; | None               | 1           |  91.83 ms | 1.472 ms |  2.111 ms |  92.27 ms |    5 |  3166.6667 | 2666.6667 | 1000.0000 | 204.54 MB |
| &#39;Decompress float32&#39; | SubtractiveDither2 | 1           | 135.36 ms | 0.857 ms |  1.256 ms | 135.54 ms |    6 |  2000.0000 | 1500.0000 |  500.0000 | 105.05 MB |
| &#39;Decompress float32&#39; | SubtractiveDither1 | 1           | 138.15 ms | 1.391 ms |  1.949 ms | 138.25 ms |    6 |  2000.0000 | 1500.0000 |  500.0000 | 105.05 MB |
| &#39;Compress float32&#39;   | None               | -1          | 219.52 ms | 5.237 ms |  7.677 ms | 218.32 ms |    7 | 10666.6667 | 4000.0000 | 1666.6667 | 254.64 MB |
| &#39;Compress float32&#39;   | NoDither           | 1           | 289.56 ms | 4.156 ms |  6.221 ms | 291.34 ms |    8 |  3500.0000 | 1500.0000 |  500.0000 | 132.35 MB |
| &#39;Compress float32&#39;   | SubtractiveDither1 | 1           | 297.21 ms | 5.934 ms |  8.123 ms | 293.04 ms |    8 |  3500.0000 | 1500.0000 |  500.0000 | 132.34 MB |
| &#39;Compress float32&#39;   | SubtractiveDither2 | 1           | 298.16 ms | 6.734 ms | 10.079 ms | 295.09 ms |    8 |  3500.0000 | 1500.0000 |  500.0000 | 132.34 MB |
| &#39;Compress float32&#39;   | None               | 1           | 714.42 ms | 6.327 ms |  9.470 ms | 714.05 ms |    9 |  9000.0000 | 3000.0000 | 1000.0000 | 254.61 MB |
