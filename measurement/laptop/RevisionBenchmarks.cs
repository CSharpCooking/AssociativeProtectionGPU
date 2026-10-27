// Revision benchmarks for a computer with a GPU (configuration A of the paper or another one, such as configuration C).
// Add this file to the benchmark project of the thesis together with GpuStegoV2.cs, NewKernels.cs,
// CpuParallelStego.cs, RevKernels.cs and RevEntry.cs, or build them with standalone/AssocRevision.csproj; they use the
// classes of the thesis library (AssocStego, AssocStegoStream, EtalonFactory, GpuStegoEncryptor, GpuStegoDecryptor,
// AcceleratorSelector). The discrete GPU is selected through CUDA, the integrated GPU through OpenCL.
// Every class is one group of measurements and is run separately (RevEntry.cs, run_revision.cmd):
//   RevBreakEven     full cycle at 1-100 KB: CPU-seq and the original GPU classes, three processes per case
//   RevFormation     formation of 10 MB: reference, sequential and multithreaded CPU variants, GPUs
//   RevRecovery      recovery of 10 MB: search and table-driven procedure on the CPU, three kernels on the GPUs
//   RevFullCycle     full cycle of 100 MB: multithreaded CPU variants and GPUs
//   RevChunkSize     full cycle of 100 MB with chunks of 256-2048 KB in one build and one run; CPU-seq as control
//   RevRecognition   recovery of the 10 MB binary inputs of data/ (uniform, all-255, all-zero, Russian text)
// Inputs: ASSOC_INPUT = UTF-8 text file (the novel used in the paper; repeated to the required size);
//         ASSOC_DATA  = folder with the files *_10MB.bin of data/ (RevRecognition only).
// The iteration setup is that of the thesis benchmarks: a new key per iteration, written to a key file and read
// back; the encoder and decoder objects are created in the setup, outside the measured interval.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;
using ILGPU.Runtime;

// ------------------------------------------------------------------ configurations
public abstract class RevConfigBase : ManualConfig
{
    protected RevConfigBase(string id, int launches, int warmups, int iterations)
    {
        AddJob(Job.Default.WithInvocationCount(1).WithUnrollFactor(1)
            .WithLaunchCount(launches).WithWarmupCount(warmups).WithIterationCount(iterations).WithId(id));
        AddColumn(StatisticColumn.Mean, StatisticColumn.Error, StatisticColumn.StdDev, StatisticColumn.StdErr,
            StatisticColumn.Min, StatisticColumn.Q1, StatisticColumn.Median, StatisticColumn.Q3, StatisticColumn.Max,
            StatisticColumn.Iterations);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddValidator(JitOptimizationsValidator.DontFailOnError);
    }
}
public sealed class RevGridConfig : RevConfigBase { public RevGridConfig() : base("L3W5I30", 3, 5, 30) { } }
public sealed class RevMidConfig : RevConfigBase { public RevMidConfig() : base("L1W3I15", 1, 3, 15) { } }
public sealed class RevLongConfig : RevConfigBase { public RevLongConfig() : base("L1W2I10", 1, 2, 10) { } }

// ------------------------------------------------------------------ common part
public abstract class RevBase
{
    protected string[] Etalons = Array.Empty<string>();
    protected byte[] Input = Array.Empty<byte>();
    protected AssocStego Stego = null!;     // key as created (formation)
    protected AssocStego Loaded = null!;    // key read back from the key file (recovery), as in the thesis benchmarks
    private protected AcceleratorSession? Session;   // AcceleratorSession is internal in the thesis library
    protected string KeyPath = string.Empty, DataPath = string.Empty;

    // device: null (CPU only), "iGPU" or "dGPU"
    protected void Init(byte[] input, string? device)
    {
        Etalons = EtalonFactory.CreateDefaultEtalons10();
        Input = input;
        string dir = Path.Combine(Path.GetTempPath(), "assoc_revision");
        Directory.CreateDirectory(dir);
        string id = Guid.NewGuid().ToString("N");
        KeyPath = Path.Combine(dir, id + "_key.bin");
        DataPath = Path.Combine(dir, id + "_protected.bin");
        Session = device switch
        {
            "iGPU" => AcceleratorSelector.Create(ExecutionMode.IntegratedGpu),
            "dGPU" => AcceleratorSelector.Create(ExecutionMode.NvidiaDiscreteGpu),
            _ => null,
        };
        if (device != null && (Session == null || Session.IsCpuFallback || Session.Accelerator == null))
            throw new InvalidOperationException(device + " is not available on this machine");
    }

    protected Accelerator Acc => Session!.Accelerator!;

    // UTF-8 encoding of the text in ASSOC_INPUT, repeated or truncated to the required number of bytes
    protected static byte[] TextInput(int bytes)
    {
        string path = Environment.GetEnvironmentVariable("ASSOC_INPUT") ?? throw new InvalidOperationException("set ASSOC_INPUT to the UTF-8 text file");
        byte[] all = Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8));
        if (all.Length == 0) throw new InvalidOperationException("ASSOC_INPUT is empty");
        var r = new byte[bytes];
        for (int o = 0; o < bytes; o += all.Length) Buffer.BlockCopy(all, 0, r, o, Math.Min(all.Length, bytes - o));
        return r;
    }

    // input size of the 10 MB and 100 MB groups; ASSOC_SIZE_OVERRIDE (bytes) is only for a quick check of the kit
    protected static int Bytes(int n) =>
        int.TryParse(Environment.GetEnvironmentVariable("ASSOC_SIZE_OVERRIDE"), out int v) && v > 0 ? v : n;

    // one of the 10 MB files of data/ (uniform, ff, zero, ru, en, zip)
    protected static byte[] DataInput(string name)
    {
        string dir = Environment.GetEnvironmentVariable("ASSOC_DATA") ?? throw new InvalidOperationException("set ASSOC_DATA to the data folder of the repository");
        return File.ReadAllBytes(Path.Combine(dir, name + "_10MB.bin"));
    }

    protected void NewKey()
    {
        Stego = new AssocStego(Etalons);
        var key = Stego.CreateKey();
        Stego.SaveKeyToFile(KeyPath, key);
        Loaded = new AssocStego(Etalons);
        Loaded.LoadKeyFromFile(KeyPath);
    }

    protected void DeleteFiles()
    {
        try { if (File.Exists(KeyPath)) File.Delete(KeyPath); if (File.Exists(DataPath)) File.Delete(DataPath); } catch { }
    }

    // byte-by-byte comparison, as VerifyFragments of the thesis benchmarks
    protected static void Verify(byte[] original, byte[] restored)
    {
        if (restored.Length != original.Length) throw new InvalidOperationException("verification failed: length");
        for (int i = 0; i < original.Length; i++)
            if (original[i] != restored[i]) throw new InvalidOperationException("verification failed at byte " + i);
    }

    // CPU reference (CPU-seq): AssocStegoStream over a file stream with a 1 MB buffer, as in the thesis benchmarks
    protected static void CpuSeqForm(byte[] data, string path, AssocStego st)
    {
        using var fs = new FileStream(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        using var s = new AssocStegoStream(fs, st);
        s.Write(data, 0, data.Length);
    }

    protected static byte[] CpuSeqRecover(string path, int n, AssocStego st)
    {
        var result = new byte[n];
        using var fs = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        using var s = new AssocStegoStream(fs, st);
        int total = 0;
        while (total < n) { int r = s.Read(result, total, n - total); if (r == 0) break; total += r; }
        if (total < n) Array.Resize(ref result, total);
        return result;
    }

    protected static string? DeviceOf(string mode) => mode.StartsWith("iGPU") ? "iGPU" : mode.StartsWith("dGPU") ? "dGPU" : null;

    protected static DecodeAlgorithm AlgorithmOf(string mode) =>
        mode.EndsWith("-table") ? DecodeAlgorithm.Table : mode.EndsWith("-branchfree") ? DecodeAlgorithm.BranchFree : DecodeAlgorithm.Search;

    // formation by a CPU mode
    protected static void CpuForm(string mode, byte[] data, string path, AssocStego st)
    {
        switch (mode)
        {
            case "CPU-seq": CpuSeqForm(data, path, st); break;
            case "CPU-seq-chunk": CpuParallelStego.EncryptToFile(data, path, st, gpuFiller: false, parallel: false); break;
            case "CPU-seq-GF": CpuParallelStego.EncryptToFile(data, path, st, gpuFiller: true, parallel: false); break;
            case "CPU-par": CpuParallelStego.EncryptToFile(data, path, st, gpuFiller: false, parallel: true); break;
            case "CPU-par-GF": CpuParallelStego.EncryptToFile(data, path, st, gpuFiller: true, parallel: true); break;
            default: throw new ArgumentException("unknown CPU mode " + mode);
        }
    }

    // recovery by a CPU mode
    protected static byte[] CpuRecover(string mode, string path, int n, AssocStego st) => mode switch
    {
        "CPU-seq" => CpuSeqRecover(path, n, st),
        "CPU-seq-table" => CpuParallelStego.DecryptFromFile(path, n, st, CpuRecognition.Table, parallel: false),
        "CPU-par" => CpuParallelStego.DecryptFromFile(path, n, st, CpuRecognition.Search, parallel: true),
        "CPU-par-table" => CpuParallelStego.DecryptFromFile(path, n, st, CpuRecognition.Table, parallel: true),
        _ => throw new ArgumentException("unknown CPU mode " + mode),
    };
}

// ------------------------------------------------------------------ 1. break-even volume (dense grid)
[MemoryDiagnoser]
[Config(typeof(RevGridConfig))]
public class RevBreakEven : RevBase
{
    [Params(1, 10, 20, 30, 40, 50, 60, 75, 100)] public int SizeKB;
    [Params("CPU-seq", "iGPU", "dGPU")] public string Mode = "CPU-seq";
    private GpuStegoEncryptor? _enc;
    private GpuStegoDecryptor? _dec;

    [GlobalSetup] public void Setup() => Init(TextInput(SizeKB * 1024), DeviceOf(Mode));
    [IterationSetup]
    public void IterationSetup()
    {
        DisposeCoders();
        NewKey();
        if (Session != null) { _enc = new GpuStegoEncryptor(Acc, Stego); _dec = new GpuStegoDecryptor(Acc, Loaded); }
    }
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() { DisposeCoders(); Session?.Dispose(); DeleteFiles(); }
    private void DisposeCoders() { _enc?.Dispose(); _dec?.Dispose(); _enc = null; _dec = null; }

    [Benchmark(Description = "Full cycle")]
    public void FullCycle()
    {
        byte[] restored;
        if (Session == null) { CpuSeqForm(Input, DataPath, Stego); restored = CpuSeqRecover(DataPath, Input.Length, Loaded); }
        else { _enc!.EncryptToStream(Input, DataPath); restored = _dec!.DecryptFromFile(DataPath, Input.Length); }
        Verify(Input, restored);
    }
}

// ------------------------------------------------------------------ 2. formation of 10 MB
[MemoryDiagnoser]
[Config(typeof(RevMidConfig))]
public class RevFormation : RevBase
{
    [Params("CPU-seq", "CPU-seq-chunk", "CPU-seq-GF", "CPU-par", "CPU-par-GF", "iGPU", "dGPU")] public string Mode = "CPU-seq";
    private GpuStegoEncryptor? _enc;

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(10 << 20)), DeviceOf(Mode));
    [IterationSetup]
    public void IterationSetup()
    {
        _enc?.Dispose(); _enc = null;
        NewKey();
        if (Session != null) _enc = new GpuStegoEncryptor(Acc, Stego);
    }
    [IterationCleanup] public void IterationCleanup() { _enc?.Dispose(); _enc = null; }
    [GlobalCleanup] public void Cleanup() { _enc?.Dispose(); Session?.Dispose(); DeleteFiles(); }

    [Benchmark(Description = "Formation")]
    public void Formation()
    {
        if (Session == null) CpuForm(Mode, Input, DataPath, Stego);
        else _enc!.EncryptToStream(Input, DataPath);
    }
}

// ------------------------------------------------------------------ 3. recovery of 10 MB
[MemoryDiagnoser]
[Config(typeof(RevMidConfig))]
public class RevRecovery : RevBase
{
    [Params("CPU-seq", "CPU-seq-table", "CPU-par", "CPU-par-table",
            "iGPU", "iGPU-v2", "iGPU-table", "iGPU-branchfree",
            "dGPU", "dGPU-v2", "dGPU-table", "dGPU-branchfree")] public string Mode = "CPU-seq";
    private GpuStegoEncryptor? _enc;
    private GpuStegoDecryptor? _dec;       // original class (mode "iGPU", "dGPU")
    private GpuStegoDecryptorV2? _dec2;    // revision class with a selectable kernel (modes "-v2", "-table", "-branchfree")

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(10 << 20)), DeviceOf(Mode));
    [IterationSetup]
    public void IterationSetup()
    {
        DisposeCoders();
        NewKey();
        if (Session == null) { CpuSeqForm(Input, DataPath, Stego); return; }   // CPU modes: protected file by the reference
        _enc = new GpuStegoEncryptor(Acc, Stego);
        _enc.EncryptToStream(Input, DataPath);                                 // GPU modes: protected file by the GPU encoder
        if (Mode == "iGPU" || Mode == "dGPU") _dec = new GpuStegoDecryptor(Acc, Loaded);
        else _dec2 = new GpuStegoDecryptorV2(Acc, Loaded, AlgorithmOf(Mode));
    }
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() { DisposeCoders(); Session?.Dispose(); DeleteFiles(); }
    private void DisposeCoders() { _enc?.Dispose(); _dec?.Dispose(); _dec2?.Dispose(); _enc = null; _dec = null; _dec2 = null; }

    [Benchmark(Description = "Recovery")]
    public byte[] Recovery()
    {
        if (Session == null) return CpuRecover(Mode, DataPath, Input.Length, Loaded);
        return _dec != null ? _dec.DecryptFromFile(DataPath, Input.Length) : _dec2!.DecryptFromFile(DataPath, Input.Length);
    }
}

// ------------------------------------------------------------------ 4. full cycle of 100 MB
[MemoryDiagnoser]
[Config(typeof(RevLongConfig))]
public class RevFullCycle : RevBase
{
    [Params("CPU-par", "CPU-par-GF", "CPU-par-GF-table", "iGPU", "iGPU-table", "dGPU", "dGPU-table")] public string Mode = "CPU-par";
    private GpuStegoEncryptor? _enc;
    private GpuStegoDecryptor? _dec;
    private GpuStegoDecryptorV2? _dec2;

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(100 << 20)), DeviceOf(Mode));
    [IterationSetup]
    public void IterationSetup()
    {
        DisposeCoders();
        NewKey();
        if (Session == null) return;
        _enc = new GpuStegoEncryptor(Acc, Stego);
        if (Mode.EndsWith("-table")) _dec2 = new GpuStegoDecryptorV2(Acc, Loaded, DecodeAlgorithm.Table);
        else _dec = new GpuStegoDecryptor(Acc, Loaded);
    }
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() { DisposeCoders(); Session?.Dispose(); DeleteFiles(); }
    private void DisposeCoders() { _enc?.Dispose(); _dec?.Dispose(); _dec2?.Dispose(); _enc = null; _dec = null; _dec2 = null; }

    [Benchmark(Description = "Full cycle")]
    public void FullCycle()
    {
        byte[] restored;
        if (Session == null)
        {
            CpuParallelStego.EncryptToFile(Input, DataPath, Stego, gpuFiller: Mode.Contains("-GF"), parallel: true);
            restored = CpuParallelStego.DecryptFromFile(DataPath, Input.Length, Loaded,
                Mode.EndsWith("-table") ? CpuRecognition.Table : CpuRecognition.Search, parallel: true);
        }
        else
        {
            _enc!.EncryptToStream(Input, DataPath);
            restored = _dec != null ? _dec.DecryptFromFile(DataPath, Input.Length) : _dec2!.DecryptFromFile(DataPath, Input.Length);
        }
        Verify(Input, restored);
    }
}

// ------------------------------------------------------------------ 5. chunk size under identical conditions
// "orig": the original classes (chunk of 512 KB, a compile-time constant); "v2-<KB>": the revision classes,
// identical except for the chunk size and for the omitted console messages. CPU-seq does not use the chunk size
// and controls the state of the machine during the run.
[MemoryDiagnoser]
[Config(typeof(RevLongConfig))]
public class RevChunkSize : RevBase
{
    [Params("CPU-seq", "iGPU-orig", "iGPU-v2-256", "iGPU-v2-512", "iGPU-v2-1024", "iGPU-v2-2048",
            "dGPU-orig", "dGPU-v2-256", "dGPU-v2-512", "dGPU-v2-1024", "dGPU-v2-2048")] public string Case = "CPU-seq";
    private GpuStegoEncryptor? _enc;
    private GpuStegoDecryptor? _dec;
    private GpuStegoEncryptorV2? _enc2;
    private GpuStegoDecryptorV2? _dec2;

    [GlobalSetup] public void Setup() => Init(TextInput(Bytes(100 << 20)), DeviceOf(Case));
    [IterationSetup]
    public void IterationSetup()
    {
        DisposeCoders();
        NewKey();
        if (Session == null) return;
        if (Case.EndsWith("-orig")) { _enc = new GpuStegoEncryptor(Acc, Stego); _dec = new GpuStegoDecryptor(Acc, Loaded); }
        else
        {
            int chunk = int.Parse(Case.Substring(Case.LastIndexOf('-') + 1)) * 1024;
            _enc2 = new GpuStegoEncryptorV2(Acc, Stego, chunk);
            _dec2 = new GpuStegoDecryptorV2(Acc, Loaded, DecodeAlgorithm.Search, chunk);
        }
    }
    [IterationCleanup] public void IterationCleanup() => DisposeCoders();
    [GlobalCleanup] public void Cleanup() { DisposeCoders(); Session?.Dispose(); DeleteFiles(); }
    private void DisposeCoders()
    {
        _enc?.Dispose(); _dec?.Dispose(); _enc2?.Dispose(); _dec2?.Dispose();
        _enc = null; _dec = null; _enc2 = null; _dec2 = null;
    }

    [Benchmark(Description = "Full cycle")]
    public void FullCycle()
    {
        byte[] restored;
        if (Session == null) { CpuSeqForm(Input, DataPath, Stego); restored = CpuSeqRecover(DataPath, Input.Length, Loaded); }
        else if (_enc != null) { _enc.EncryptToStream(Input, DataPath); restored = _dec!.DecryptFromFile(DataPath, Input.Length); }
        else { _enc2!.EncryptToStream(Input, DataPath); restored = _dec2!.DecryptFromFile(DataPath, Input.Length); }
        Verify(Input, restored);
    }
}

// ------------------------------------------------------------------ 6. recovery on different data
[MemoryDiagnoser]
[Config(typeof(RevMidConfig))]
public class RevRecognition : RevBase
{
    [Params("uniform", "ff", "zero", "ru")] public string Data = "uniform";
    [Params("CPU-seq", "CPU-seq-table", "iGPU", "iGPU-table", "iGPU-branchfree", "dGPU", "dGPU-table", "dGPU-branchfree")] public string Mode = "CPU-seq";
    private GpuStegoEncryptor? _enc;
    private GpuStegoDecryptorV2? _dec2;

    [GlobalSetup] public void Setup() => Init(DataInput(Data), DeviceOf(Mode));
    [IterationSetup]
    public void IterationSetup()
    {
        _enc?.Dispose(); _dec2?.Dispose(); _enc = null; _dec2 = null;
        NewKey();
        if (Session == null) { CpuSeqForm(Input, DataPath, Stego); return; }
        _enc = new GpuStegoEncryptor(Acc, Stego);
        _enc.EncryptToStream(Input, DataPath);
        _dec2 = new GpuStegoDecryptorV2(Acc, Loaded, AlgorithmOf(Mode));
    }
    [IterationCleanup] public void IterationCleanup() { _enc?.Dispose(); _dec2?.Dispose(); _enc = null; _dec2 = null; }
    [GlobalCleanup] public void Cleanup() { _enc?.Dispose(); _dec2?.Dispose(); Session?.Dispose(); DeleteFiles(); }

    [Benchmark(Description = "Recovery")]
    public byte[] Recovery() => Session == null
        ? CpuRecover(Mode, DataPath, Input.Length, Loaded)
        : _dec2!.DecryptFromFile(DataPath, Input.Length);
}
