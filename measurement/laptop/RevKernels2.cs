// Revision 2: kernel-only times as in RevKernels, for several thread group sizes and with the formation kernel with
// coalesced stores (command rev-kernels2 of RevEntry). For each GPU, input and group size (0 = automatic, as in the library),
// the two formation kernels and the three recognition kernels are launched over the whole 10 MB input with the data in device
// memory and timed with a Stopwatch up to the end of Synchronize. The output of the coalesced formation kernel is compared with
// that of the original kernel for the same seed, and every recognition with the source, byte by byte.
// Output: CSV "device,input,group,step,rep,ms" (decimal point); the first repetition is a warm-up and is not printed.
// args: [data folder] [repetitions = 10] [devices = iGPU,dGPU] [inputs = uniform,ff,zero,ru] [group sizes = 0,32,64,128,256,512,1024]
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ILGPU;
using ILGPU.Runtime;

public static class RevKernels2
{
    public static int Run(string[] args)
    {
        string dataDir = args.Length > 0 ? args[0]
            : Environment.GetEnvironmentVariable("ASSOC_DATA") ?? throw new InvalidOperationException("give the data folder");
        int reps = args.Length > 1 ? int.Parse(args[1]) : 10;
        string[] devices = args.Length > 2 ? args[2].Split(',') : new[] { "iGPU", "dGPU" };
        string[] inputs = args.Length > 3 ? args[3].Split(',') : new[] { "uniform", "ff", "zero", "ru" };
        int[] groups = args.Length > 4 ? args[4].Split(',').Select(int.Parse).ToArray() : new[] { 0, 32, 64, 128, 256, 512, 1024 };
        Console.WriteLine("device,input,group,step,rep,ms");
        foreach (string dev in devices)
        {
            using var session = dev == "iGPU"
                ? AcceleratorSelector.Create(ExecutionMode.IntegratedGpu)
                : AcceleratorSelector.Create(ExecutionMode.NvidiaDiscreteGpu);
            if (session.IsCpuFallback || session.Accelerator == null) { Console.Error.WriteLine(dev + ": not available"); continue; }
            Accelerator acc = session.Accelerator;
            foreach (int g in groups)
            {
                if (g > acc.MaxNumThreadsPerGroup) { Console.Error.WriteLine($"{dev}: group size {g} exceeds {acc.MaxNumThreadsPerGroup}, skipped"); continue; }
                Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int> Load8(
                    Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int> k) =>
                    g <= 0 ? acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(k)
                           : acc.LoadImplicitlyGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(k, g);
                var formation = Load8(NewKernels.EncryptKernelCopy);
                var coalesced = Load8(NewKernels.EncryptCoalescedKernel);
                var search = Load8(NewKernels.DecryptSearchKernelCopy);
                var branchFree = Load8(NewKernels.DecryptBranchFreeKernel);
                var table = g <= 0
                    ? acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>, ArrayView<int>, ArrayView<byte>, int, int>(NewKernels.DecryptTableKernel)
                    : acc.LoadImplicitlyGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>, ArrayView<int>, ArrayView<byte>, int, int>(NewKernels.DecryptTableKernel, g);

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
                    byte[] a = new byte[n * h], b = new byte[n * h], restored = new byte[n];
                    int seed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
                    dInput.CopyFromCPU(input);

                    // identity of the two formation kernels for this seed
                    formation(n, dInput.View, dProtected.View, dMaskedEtalons.View, dKeys.View, c, 0, seed); acc.Synchronize(); dProtected.CopyToCPU(a);
                    coalesced(n * h, dInput.View, dProtected.View, dMaskedEtalons.View, dKeys.View, c, 0, seed); acc.Synchronize(); dProtected.CopyToCPU(b);
                    if (!a.AsSpan().SequenceEqual(b)) throw new InvalidOperationException($"{dev}, {name}, group {g}: the coalesced formation kernel differs from the original one");

                    double Time(Action f) { var sw = Stopwatch.StartNew(); f(); acc.Synchronize(); return sw.Elapsed.TotalMilliseconds; }
                    var recognition = new (string Step, Action Launch)[]
                    {
                        ("search kernel", () => search(n, dProtected.View, dOutput.View, dMaskedEtalons.View, dKeys.View, c, 3, etalonCount)),
                        ("table kernel", () => table(n, dProtected.View, dOutput.View, dPosByte.View, dPosBit.View, dTable.View, tb.M, c)),
                        ("branch-free kernel", () => branchFree(n, dProtected.View, dOutput.View, dMaskedEtalons.View, dKeys.View, c, 3, etalonCount)),
                    };
                    for (int rep = -1; rep < reps; rep++)
                    {
                        void Report(string step, double ms) { if (rep >= 0) Console.WriteLine(FormattableString.Invariant($"{dev},{name},{g},{step},{rep},{ms:F3}")); }
                        Report("formation kernel", Time(() => formation(n, dInput.View, dProtected.View, dMaskedEtalons.View, dKeys.View, c, 0, seed)));
                        Report("coalesced formation kernel", Time(() => coalesced(n * h, dInput.View, dProtected.View, dMaskedEtalons.View, dKeys.View, c, 0, seed)));
                        foreach (var k in recognition)
                        {
                            Report(k.Step, Time(k.Launch));
                            dOutput.CopyToCPU(restored);
                            if (!restored.AsSpan().SequenceEqual(input))
                                throw new InvalidOperationException($"{dev}, {name}, group {g}, {k.Step}: recovery is not byte-exact");
                        }
                    }
                }
            }
        }
        return 0;
    }
}
