```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 8.0.427
  [Host] : .NET 8.0.33 (8.0.33, 8.0.3326.52219), X64 RyuJIT x86-64-v3
  L3W1I5 : .NET 8.0.33 (8.0.33, 8.0.3326.52219), X64 RyuJIT x86-64-v3

Job=L3W1I5  InvocationCount=1  IterationCount=5  
LaunchCount=3  UnrollFactor=1  WarmupCount=1  

```
| Method       | Mode    | Mean    | Error   | StdDev  | StdErr  | Min     | Q1      | Median  | Q3      | Max     | Iterations | Allocated |
|------------- |-------- |--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|-----------:|----------:|
| &#39;Full cycle&#39; | CPU-seq | 25.48 s | 1.707 s | 1.597 s | 0.412 s | 22.57 s | 24.74 s | 25.57 s | 26.31 s | 27.77 s |      15.00 |    102 MB |
