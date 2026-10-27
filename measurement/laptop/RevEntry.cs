// Entry point of the revision measurements (standalone/Program.cs calls it in the standalone build). In Program.Main
// of the benchmark project of the thesis, insert as the first statement
//     if (args.Length > 0 && args[0].StartsWith("rev")) { RevEntry.Run(args); return; }
// (write "return RevEntry.Run(args);" instead if Main returns int). Commands:
//     rev --filter *RevBreakEven* [further BenchmarkDotNet options, e.g. --artifacts <folder>]
//     rev-kernels <data folder> [repetitions] [iGPU,dGPU] [uniform,ff,zero,ru]
//     rev-env
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Running;
using ILGPU.Runtime;

public static class RevEntry
{
    public static readonly Type[] Benchmarks =
    {
        typeof(RevBreakEven), typeof(RevFormation), typeof(RevRecovery),
        typeof(RevFullCycle), typeof(RevChunkSize), typeof(RevRecognition),
    };

    public static int Run(string[] args)
    {
        string[] rest = args.Skip(1).ToArray();
        switch (args[0])
        {
            case "rev":
                BenchmarkSwitcher.FromTypes(Benchmarks).Run(rest);
                return 0;
            case "rev-kernels":
                return RevKernels.Run(rest);
            case "rev-env":
                return Env();
            default:
                Console.Error.WriteLine("commands: rev, rev-kernels, rev-env");
                return 1;
        }
    }

    private static int Env()
    {
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Console.WriteLine($"Logical processors: {Environment.ProcessorCount}");
        Console.WriteLine($"ILGPU: {typeof(Accelerator).Assembly.GetName().Version}");
        Console.WriteLine($"BenchmarkDotNet: {typeof(BenchmarkSwitcher).Assembly.GetName().Version}");
        foreach (string dev in new[] { "iGPU", "dGPU" })
        {
            try
            {
                using var s = dev == "iGPU"
                    ? AcceleratorSelector.Create(ExecutionMode.IntegratedGpu)
                    : AcceleratorSelector.Create(ExecutionMode.NvidiaDiscreteGpu);
                if (s.Accelerator == null) { Console.WriteLine($"{dev}: not available. {s.Message}"); continue; }
                var a = s.Accelerator;
                Console.WriteLine($"{dev}: {a.Name}; backend {a.AcceleratorType}; memory {a.MemorySize >> 20} MB; " +
                                  $"max threads per group {a.MaxNumThreadsPerGroup}; warp size {a.WarpSize}");
            }
            catch (Exception ex) { Console.WriteLine($"{dev}: {ex.GetType().Name}: {ex.Message}"); }
        }
        return 0;
    }
}
