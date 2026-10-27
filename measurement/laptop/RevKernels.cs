// Kernel-level measurements without BenchmarkDotNet (command rev-kernels of RevEntry).
// For each GPU and each 10 MB input of data/, the formation kernel and the three recognition kernels (search,
// table-driven, branch-free) are launched over the whole input at once, with the data resident on the device,
// and every launch is timed with a Stopwatch up to the end of Synchronize. The same run times the copies of the
// input and of the protected representation to and from the device (one copy each) and the writing and reading
// of the protected representation to and from a file, so that the kernel times can be set against the stage
// times of RevFormation and RevRecovery. Every recognition is checked byte by byte.
// Output: CSV lines "device,input,step,rep,ms" on the standard output; the first repetition is a warm-up and is
// not printed. With reps = 1 the command is short enough to be run under Nsight Compute (see the instructions).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using ILGPU;
using ILGPU.Runtime;

public static class RevKernels
{
    // args: [data folder] [repetitions = 10] [devices = iGPU,dGPU] [inputs = uniform,ff,zero,ru]
    public static int Run(string[] args)
    {
        string dataDir = args.Length > 0 ? args[0]
            : Environment.GetEnvironmentVariable("ASSOC_DATA") ?? throw new InvalidOperationException("give the data folder");
        int reps = args.Length > 1 ? int.Parse(args[1]) : 10;
        string[] devices = args.Length > 2 ? args[2].Split(',') : new[] { "iGPU", "dGPU" };
        string[] inputs = args.Length > 3 ? args[3].Split(',') : new[] { "uniform", "ff", "zero", "ru" };
        string file = Path.Combine(Path.GetTempPath(), "assoc_revision_kernels.bin");
        Console.WriteLine("device,input,step,rep,ms");
        foreach (string dev in devices)
        {
            using var session = dev == "iGPU"
                ? AcceleratorSelector.Create(ExecutionMode.IntegratedGpu)
                : AcceleratorSelector.CreateBestDiscreteGpuForBenchmarks();
            if (session.IsCpuFallback || session.Accelerator == null) { Console.Error.WriteLine(dev + ": not available"); continue; }
            Accelerator acc = session.Accelerator;
            var formation = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(NewKernels.EncryptKernelCopy);
            var search = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(NewKernels.DecryptSearchKernelCopy);
            var branchFree = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(NewKernels.DecryptBranchFreeKernel);
            var table = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>, ArrayView<int>, ArrayView<byte>, int, int>(NewKernels.DecryptTableKernel);

            foreach (string name in inputs)
            {
                byte[] input = File.ReadAllBytes(Path.Combine(dataDir, name + "_10MB.bin"));
                int n = input.Length;
                var st = new AssocStego(EtalonFactory.CreateDefaultEtalons10());
                st.CreateKey();
                int c = st.ContainerByteLength, h = st.HiddenByteLength, etalonCount = st.etalons.Length;
                var tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, c, etalonCount);
                using var dMaskedEtalons = acc.Allocate1D<byte>(st.FlattenedMaskedEtalonsBytes);
                using var dKeys = acc.Allocate1D<byte>(st.FlattenedKeysBytes);
                using var dTable = acc.Allocate1D<byte>(tb.Table);
                using var dPosByte = acc.Allocate1D<int>(tb.PosByte);
                using var dPosBit = acc.Allocate1D<int>(tb.PosBit);
                using var dInput = acc.Allocate1D<byte>(n);
                using var dProtected = acc.Allocate1D<byte>((long)n * h);
                using var dOutput = acc.Allocate1D<byte>(n);
                byte[] protectedHost = new byte[n * h];
                byte[] restored = new byte[n];
                int seed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

                double Time(Action a) { var sw = Stopwatch.StartNew(); a(); acc.Synchronize(); return sw.Elapsed.TotalMilliseconds; }
                var kernels = new (string Step, Action Launch)[]
                {
                    ("search kernel", () => search(n, dProtected.View, dOutput.View, dMaskedEtalons.View, dKeys.View, c, 3, etalonCount)),
                    ("table kernel", () => table(n, dProtected.View, dOutput.View, dPosByte.View, dPosBit.View, dTable.View, tb.M, c)),
                    ("branch-free kernel", () => branchFree(n, dProtected.View, dOutput.View, dMaskedEtalons.View, dKeys.View, c, 3, etalonCount)),
                };
                for (int rep = -1; rep < reps; rep++)
                {
                    void Report(string step, double ms) { if (rep >= 0) Console.WriteLine($"{dev},{name},{step},{rep},{ms:F3}"); }
                    Report("copy input to device", Time(() => dInput.CopyFromCPU(input)));
                    Report("formation kernel", Time(() => formation(n, dInput.View, dProtected.View, dMaskedEtalons.View, dKeys.View, c, 0, seed)));
                    Report("copy protected to host", Time(() => dProtected.CopyToCPU(protectedHost)));
                    Report("copy protected to device", Time(() => dProtected.CopyFromCPU(protectedHost)));
                    foreach (var k in kernels)
                    {
                        Report(k.Step, Time(k.Launch));
                        dOutput.CopyToCPU(restored);
                        if (!restored.AsSpan().SequenceEqual(input))
                            throw new InvalidOperationException($"{dev}, {name}, {k.Step}: recovery is not byte-exact");
                    }
                    Report("copy output to host", Time(() => dOutput.CopyToCPU(restored)));
                    Report("write protected file", TimeIo(() =>
                    {
                        using var fs = new FileStream(file, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
                        fs.Write(protectedHost, 0, protectedHost.Length);
                    }));
                    Report("read protected file", TimeIo(() =>
                    {
                        using var fs = new FileStream(file, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
                        int got = 0;
                        while (got < protectedHost.Length) { int r = fs.Read(protectedHost, got, protectedHost.Length - got); if (r == 0) break; got += r; }
                    }));
                }
            }
        }
        try { File.Delete(file); } catch { }
        return 0;
    }

    private static double TimeIo(Action a) { var sw = Stopwatch.StartNew(); a(); return sw.Elapsed.TotalMilliseconds; }
}
