// Revision 2 benchmarks (second report of reviewer 1). Same base class, inputs and iteration setup as RevisionBenchmarks.cs
// (a new key per iteration, written to a key file and read back; coders created in the setup, outside the measured interval),
// but every benchmark runs in three independent processes (LaunchCount 3). Modes added to those of the first kit:
//   CPU-opt             optimized CPU path (measurement/AssocMeasure/New/CpuOptStego.cs) on all hardware threads
//   iGPU-opt, dGPU-opt  V2 classes with device buffers reused between chunks (host arrays sized to the input, as in V2),
//                       the formation kernel with coalesced stores and the table-driven recognition kernel
// Power plan: BenchmarkDotNet switches every benchmark process to the High performance plan; run_revision2.cmd activates
// the same plan for the whole session, so that the kernel-only times (rev-kernels2) are measured under it as well, and it
// logs the clock rates of the discrete GPU with nvidia-smi during all runs.
//   Rev2BreakEven    full cycle at 1-1024 KB: CPU-seq, CPU-opt, original GPU classes and the -opt classes
//   Rev2Formation    formation of 10 MB          Rev2Recovery   recovery of 10 MB
//   Rev2FullCycle    full cycle of 100 MB         Rev2FullCycleSeq   full cycle of 100 MB by CPU-seq (fewer iterations)
using System;
using System.IO;
using BenchmarkDotNet.Attributes;

public sealed class Rev2GridConfig : RevConfigBase { public Rev2GridConfig() : base("L3W5I30", 3, 5, 30) { } }
public sealed class Rev2MidConfig : RevConfigBase { public Rev2MidConfig() : base("L3W3I15", 3, 3, 15) { } }
public sealed class Rev2LongConfig : RevConfigBase { public Rev2LongConfig() : base("L3W2I10", 3, 2, 10) { } }
public sealed class Rev2SeqConfig : RevConfigBase { public Rev2SeqConfig() : base("L3W1I5", 3, 1, 5) { } }

public abstract class Rev2Base : RevBase
{
    protected CpuOptStego? Opt;
    protected byte[] Result = Array.Empty<byte>();
    protected GpuStegoEncryptor? Enc;
    protected GpuStegoDecryptor? Dec;
    protected GpuStegoEncryptorV2? Enc2;
    protected GpuStegoDecryptorV2? Dec2;

    protected static bool IsOpt(string mode) => mode.EndsWith("-opt");

    // a new key, then the coders of the mode (outside the measured interval)
    protected void PrepareCoders(string mode, bool encoder, bool decoder)
    {
        DisposeCoders();
        NewKey();
        if (mode == "CPU-opt")
        {
            var tb = RecognitionTable.Build(Loaded.FlattenedKeysBytes, Loaded.FlattenedMaskedEtalonsBytes, Loaded.ContainerByteLength, Loaded.etalons.Length);
            Opt = new CpuOptStego(Stego, tb, Input.Length, parallel: true);
            if (Result.Length != Input.Length) Result = new byte[Input.Length];
            return;
        }
        if (Session == null) return;
        if (encoder)
        {
            if (IsOpt(mode)) Enc2 = new GpuStegoEncryptorV2(Acc, Stego, reuseDeviceBuffers: true, coalesced: true);
            else Enc = new GpuStegoEncryptor(Acc, Stego);
        }
        if (decoder)
        {
            if (IsOpt(mode)) Dec2 = new GpuStegoDecryptorV2(Acc, Loaded, DecodeAlgorithm.Table, reuseDeviceBuffers: true);
            else if (mode.EndsWith("-table")) Dec2 = new GpuStegoDecryptorV2(Acc, Loaded, DecodeAlgorithm.Table);
            else Dec = new GpuStegoDecryptor(Acc, Loaded);
        }
    }

    protected void DisposeCoders()
    {
        Enc?.Dispose(); Dec?.Dispose(); Enc2?.Dispose(); Dec2?.Dispose();
        Enc = null; Dec = null; Enc2 = null; Dec2 = null; Opt = null;
    }

    // formation by the mode; CPU modes of the first kit through CpuForm
    protected void Form(string mode)
    {
        if (mode == "CPU-opt") Opt!.EncryptToFile(Input, DataPath);
        else if (Session == null) CpuForm(mode == "CPU-par-GF-table" ? "CPU-par-GF" : mode, Input, DataPath, Stego);
        else if (Enc2 != null) Enc2.EncryptToStream(Input, DataPath);
        else Enc!.EncryptToStream(Input, DataPath);
    }

    protected byte[] Recover(string mode)
    {
        if (mode == "CPU-opt") { Opt!.DecryptFromFile(DataPath, Result, Input.Length); return Result; }
        if (Session == null) return CpuRecover(mode == "CPU-par-GF-table" ? "CPU-par-table" : mode, DataPath, Input.Length, Loaded);
        return Dec2 != null ? Dec2.DecryptFromFile(DataPath, Input.Length) : Dec!.DecryptFromFile(DataPath, Input.Length);
    }

    protected void FullCycleOnce(string mode)
    {
        Form(mode);
        Verify(Input, Recover(mode));
    }

    protected void Release() { DisposeCoders(); Session?.Dispose(); DeleteFiles(); }
}

// ------------------------------------------------------------------ 1. break-even volume against CPU-seq and CPU-opt
[MemoryDiagnoser]
[Config(typeof(Rev2GridConfig))]
public class Rev2BreakEven : Rev2Base
{
    [Params(1, 5, 10, 20, 30, 50, 100, 1024)] public int SizeKB;
    [Params("CPU-seq", "CPU-opt", "iGPU", "dGPU", "iGPU-opt", "dGPU-opt")] public string Mode = "CPU-seq";

    [GlobalSetup] public void Setup() => Init(TextInput(SizeKB * 1024), DeviceOf(Mode));
    [IterationSetup] public void IterationSetup() => PrepareCoders(Mode, true, true);
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() => Release();

    [Benchmark(Description = "Full cycle")]
    public void FullCycle() => FullCycleOnce(Mode);
}

// ------------------------------------------------------------------ 2. formation of 10 MB
[MemoryDiagnoser]
[Config(typeof(Rev2MidConfig))]
public class Rev2Formation : Rev2Base
{
    [Params("CPU-seq", "CPU-par", "CPU-par-GF", "CPU-opt", "iGPU", "dGPU", "iGPU-opt", "dGPU-opt")] public string Mode = "CPU-seq";

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(10 << 20)), DeviceOf(Mode));
    [IterationSetup] public void IterationSetup() => PrepareCoders(Mode, true, false);
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() => Release();

    [Benchmark(Description = "Formation")]
    public void Formation() => Form(Mode);
}

// ------------------------------------------------------------------ 3. recovery of 10 MB
[MemoryDiagnoser]
[Config(typeof(Rev2MidConfig))]
public class Rev2Recovery : Rev2Base
{
    [Params("CPU-seq", "CPU-par-table", "CPU-opt", "iGPU", "iGPU-table", "iGPU-opt", "dGPU", "dGPU-table", "dGPU-opt")] public string Mode = "CPU-seq";

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(10 << 20)), DeviceOf(Mode));
    [IterationSetup]
    public void IterationSetup()
    {
        PrepareCoders(Mode, false, true);
        if (Session == null) { CpuSeqForm(Input, DataPath, Stego); return; }   // CPU modes: protected file by the reference
        using var enc = new GpuStegoEncryptor(Acc, Stego);                    // GPU modes: protected file by the GPU encoder
        enc.EncryptToStream(Input, DataPath);
    }
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() => Release();

    [Benchmark(Description = "Recovery")]
    public byte[] Recovery() => Recover(Mode);
}

// ------------------------------------------------------------------ 4. full cycle of 100 MB
[MemoryDiagnoser]
[Config(typeof(Rev2LongConfig))]
public class Rev2FullCycle : Rev2Base
{
    [Params("CPU-par", "CPU-par-GF-table", "CPU-opt", "iGPU", "iGPU-table", "iGPU-opt", "dGPU", "dGPU-table", "dGPU-opt")] public string Mode = "CPU-par";

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(100 << 20)), DeviceOf(Mode));
    [IterationSetup] public void IterationSetup() => PrepareCoders(Mode, true, true);
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() => Release();

    [Benchmark(Description = "Full cycle")]
    public void FullCycle() => FullCycleOnce(Mode);
}

[MemoryDiagnoser]
[Config(typeof(Rev2SeqConfig))]
public class Rev2FullCycleSeq : Rev2Base
{
    [Params("CPU-seq")] public string Mode = "CPU-seq";

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(100 << 20)), null);
    [IterationSetup] public void IterationSetup() => PrepareCoders(Mode, true, true);
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() => Release();

    [Benchmark(Description = "Full cycle")]
    public void FullCycle() => FullCycleOnce(Mode);
}
