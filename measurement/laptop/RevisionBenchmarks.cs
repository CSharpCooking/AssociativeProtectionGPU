// Optional BenchmarkDotNet class for the laptop of configuration A (add to the benchmark project of the thesis,
// together with NewKernels.cs, GpuStegoV2.cs and CpuParallelStego.cs). One class covers the requests that need
// new GPU measurements: the dense grid of volumes (5.1), multithreaded CPU paths (2.5), the table-driven and
// branch-free recognition on the GPUs (1.3, 3.6), the chunk size (2.1) and the thread group size (2.6).
// Run, for example:
//   dotnet run -c Release -- --filter *RevisionBenchmarks* --join
// and set ASSOC_INPUT to a UTF-8 text file of at least 100 MB (the first SizeKB*1024 bytes are used).
// Reduce the [Params] lists to what is needed: the full cross product is large.
using System.Text;
using BenchmarkDotNet.Attributes;
using ILGPU.Runtime;

[MemoryDiagnoser]
public class RevisionBenchmarks
{
    // 1-100 KB: dense grid around the break-even point; 102400 = 100 MB
    [Params(1, 2, 5, 10, 20, 30, 40, 50, 60, 75, 100, 1024, 10240, 102400)] public int SizeKB;
    [Params("CPU-seq", "CPU-par", "CPU-par-GF", "iGPU", "dGPU")] public string Mode = "CPU-seq";
    [Params(DecodeAlgorithm.Search, DecodeAlgorithm.Table)] public DecodeAlgorithm Algorithm;
    [Params(512)] public int ChunkKB;          // chunk-size sweep: 64, 128, ..., 4096 (GPU modes)
    [Params(0)] public int GroupSize;          // 0 = automatic (LoadAutoGroupedStreamKernel); 32 ... 1024 = explicit
    [Params(false)] public bool ReuseDeviceBuffers;

    private string[] _etalons = Array.Empty<string>();
    private byte[] _input = Array.Empty<byte>();
    private AssocStego _stego = null!;
    private AcceleratorSession? _session;
    private GpuStegoEncryptorV2? _enc;
    private GpuStegoDecryptorV2? _dec;
    private string _file = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _etalons = EtalonFactory.CreateDefaultEtalons10();
        string path = Environment.GetEnvironmentVariable("ASSOC_INPUT") ?? throw new InvalidOperationException("set ASSOC_INPUT");
        byte[] all = Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8));
        int n = SizeKB * 1024;
        if (all.Length < n) throw new InvalidOperationException($"input has {all.Length} bytes, {n} needed");
        _input = all.AsSpan(0, n).ToArray();
        _file = Path.Combine(Path.GetTempPath(), $"revision_{Guid.NewGuid():N}.bin");
        if (Mode == "iGPU") _session = AcceleratorSelector.Create(ExecutionMode.IntegratedGpu);
        if (Mode == "dGPU") _session = AcceleratorSelector.CreateBestDiscreteGpuForBenchmarks();
        if (_session != null && (_session.IsCpuFallback || _session.Accelerator == null))
            throw new InvalidOperationException($"{Mode} is not available on this machine");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _stego = new AssocStego(_etalons);
        _stego.CreateKey();                                   // new key per iteration, as in the original benchmarks
        _enc?.Dispose(); _dec?.Dispose(); _enc = null; _dec = null;
        if (_session?.Accelerator != null)
        {
            _enc = new GpuStegoEncryptorV2(_session.Accelerator, _stego, ChunkKB * 1024, GroupSize, ReuseDeviceBuffers);
            _dec = new GpuStegoDecryptorV2(_session.Accelerator, _stego, Algorithm, ChunkKB * 1024, GroupSize, ReuseDeviceBuffers);
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _enc?.Dispose(); _dec?.Dispose(); _session?.Dispose();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [Benchmark(Description = "Full cycle (formation + recovery + verification)")]
    public void FullCycle()
    {
        Form();
        byte[] restored = Recover();
        if (!restored.AsSpan().SequenceEqual(_input)) throw new InvalidOperationException("verification failed");
    }

    private void Form()
    {
        switch (Mode)
        {
            case "CPU-seq":
                using (var fs = new FileStream(_file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan))
                using (var s = new AssocStegoStream(fs, _stego)) s.Write(_input, 0, _input.Length);
                break;
            case "CPU-par": CpuParallelStego.EncryptToFile(_input, _file, _stego, gpuFiller: false); break;
            case "CPU-par-GF": CpuParallelStego.EncryptToFile(_input, _file, _stego, gpuFiller: true); break;
            default: _enc!.EncryptToStream(_input, _file); break;
        }
    }

    private byte[] Recover()
    {
        switch (Mode)
        {
            case "CPU-seq":
            {
                var result = new byte[_input.Length];
                using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                using var s = new AssocStegoStream(fs, _stego);
                int total = 0;
                while (total < result.Length) { int r = s.Read(result, total, result.Length - total); if (r == 0) break; total += r; }
                return result;
            }
            case "CPU-par":
            case "CPU-par-GF":
                return CpuParallelStego.DecryptFromFile(_file, _input.Length, _stego,
                    Algorithm == DecodeAlgorithm.Table ? CpuRecognition.Table : CpuRecognition.Search);
            default:
                return _dec!.DecryptFromFile(_file, _input.Length);
        }
    }
}
