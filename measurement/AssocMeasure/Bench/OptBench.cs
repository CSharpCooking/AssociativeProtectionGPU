// Revision 2: the optimized CPU path (CPU-opt, New/CpuOptStego.cs) against the CPU paths of configuration D
// (measurement/laptop/CpuParallelStego.cs, compiled unchanged) in the same file-based scenario: chunks of 512 KB,
// ranges of 16 KB, all hardware threads or one thread, a new key in the setup of every iteration.
// One call is one process; run_opt_linux.sh starts several processes and rotates the order of the variants between them.
//   opt <input> <process index> <out.json>
// Groups: formation of 10 MB, recovery of 10 MB, full cycle of 10 MB and of 100 MB (the input repeated ten times).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

public static class OptBench
{
    sealed record Variant(string Name, Action Body);
    static readonly JsonSerializerOptions JsonOpts = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static void Run(string inputPath, int process, string outJson)
    {
        byte[] input10 = File.ReadAllBytes(inputPath);
        if (input10.Length != 10 * 1048576) throw new ArgumentException("a 10 MB input is expected");
        byte[] input100 = new byte[input10.Length * 10];
        for (int r = 0; r < 10; r++) Buffer.BlockCopy(input10, 0, input100, r * input10.Length, input10.Length);
        string[] et = EtalonFactory.CreateDefaultEtalons10();
        string tmp = Path.Combine(Path.GetTempPath(), "assoc_opt"); Directory.CreateDirectory(tmp);
        string file = Path.Combine(tmp, $"stego_{process}.bin");
        int threads = Environment.ProcessorCount;
        var groups = new List<object>();

        // state rebuilt in the setup of every iteration (outside the measured interval)
        AssocStego st = null!; RecognitionTable tb = null!; CpuOptStego opt1 = null!, optN = null!;
        byte[] input = input10, result = Array.Empty<byte>(), memHidden = Array.Empty<byte>();
        void NewKey(byte[] inp)
        {
            input = inp;
            st = new AssocStego(et); st.CreateKey();
            tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10);
            opt1 = new CpuOptStego(st, tb, inp.Length, parallel: false);
            optN = new CpuOptStego(st, tb, inp.Length, parallel: true);
            if (result.Length != inp.Length) result = new byte[inp.Length];
        }
        void Check() { if (!result.AsSpan(0, input.Length).SequenceEqual(input)) throw new InvalidOperationException("verification failed"); }

        void Group(string name, int warmup, int iters, List<Variant> vs, Action setup, Action? cleanup)
        {
            // rotate the order of the variants between processes
            if (Environment.GetEnvironmentVariable("OPT_QUICK") == "1") { warmup = 0; iters = 1; }   // smoke test only
            int k = process % vs.Count;
            var order = vs.Skip(k).Concat(vs.Take(k)).ToList();
            var stats = new List<Stat>();
            foreach (var v in order)
            {
                var s = Harness.Measure(v.Name, v.Body, warmup, iters, setup, cleanup);
                stats.Add(s);
                Console.WriteLine($"[{name}] " + Harness.Fmt(s, input.Length / 1048576.0));
            }
            groups.Add(new { group = name, order = order.Select(v => v.Name).ToArray(), results = stats });
            File.WriteAllText(outJson, JsonSerializer.Serialize(new { process, threads, input = inputPath, groups }, JsonOpts));
        }

        // ------------------------------------------------------------ formation of 10 MB
        Group("formation 10 MB", 2, 10, new List<Variant>
        {
            new("CPU-seq-chunk", () => CpuParallelStego.EncryptToFile(input, file, st, gpuFiller: false, parallel: false)),
            new("CPU-par", () => CpuParallelStego.EncryptToFile(input, file, st, gpuFiller: false, parallel: true)),
            new("CPU-par-GF", () => CpuParallelStego.EncryptToFile(input, file, st, gpuFiller: true, parallel: true)),
            new("CPU-opt, 1 thread", () => opt1.EncryptToFile(input, file)),
            new("CPU-opt", () => optN.EncryptToFile(input, file)),
            new("CPU-par logic, memory", () => CpuVariants.Par(input.Length, CpuParallelStego.Range, (a, b) => CpuVariants.FormBulkCsprng(st, input, memHidden, a, b))),
            new("CPU-opt, memory", () => optN.EncryptToMemory(input, memHidden)),
            new("filler generation only, memory", () => Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, input.Length, CpuParallelStego.Range),
                r => RandomNumberGenerator.Fill(memHidden.AsSpan(r.Item1 * 30, (r.Item2 - r.Item1) * 30)))),
        }, () => { NewKey(input10); if (memHidden.Length != input10.Length * 30) memHidden = new byte[input10.Length * 30]; }, null);

        // ------------------------------------------------------------ recovery of 10 MB (file formed in the setup by CPU-opt)
        byte[]? got = null;
        Group("recovery 10 MB", 2, 10, new List<Variant>
        {
            new("CPU-seq-table", () => got = CpuParallelStego.DecryptFromFile(file, input.Length, st, CpuRecognition.Table, parallel: false)),
            new("CPU-par", () => got = CpuParallelStego.DecryptFromFile(file, input.Length, st, CpuRecognition.Search, parallel: true)),
            new("CPU-par-table", () => got = CpuParallelStego.DecryptFromFile(file, input.Length, st, CpuRecognition.Table, parallel: true)),
            new("CPU-opt, 1 thread", () => opt1.DecryptFromFile(file, result, input.Length)),
            new("CPU-opt", () => optN.DecryptFromFile(file, result, input.Length)),
            new("CPU-par-table logic, memory", () => CpuVariants.Par(input.Length, CpuParallelStego.Range, (a, b) => CpuVariants.RecTable(tb, st, memHidden, result, a, b))),
            new("CPU-opt, memory", () => optN.DecryptFromMemory(memHidden, result, input.Length)),
        }, () =>
        {
            NewKey(input10);
            optN.EncryptToMemory(input10, memHidden);
            using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)) fs.Write(memHidden, 0, memHidden.Length);
            Array.Clear(result); got = null;
        }, () =>
        {
            if (got != null) { Buffer.BlockCopy(got, 0, result, 0, got.Length); }
            Check();
        });

        // ------------------------------------------------------------ full cycle of 10 MB and 100 MB
        foreach (var (inp, label, warm, it) in new[] { (input10, "full cycle 10 MB", 2, 10), (input100, "full cycle 100 MB", 1, 6) })
        {
            memHidden = Array.Empty<byte>(); GC.Collect();
            Group(label, warm, it, new List<Variant>
            {
                new("CPU-par", () =>
                {
                    CpuParallelStego.EncryptToFile(input, file, st, gpuFiller: false, parallel: true);
                    var r = CpuParallelStego.DecryptFromFile(file, input.Length, st, CpuRecognition.Search, parallel: true);
                    if (!r.AsSpan().SequenceEqual(input)) throw new InvalidOperationException("verification failed");
                }),
                new("CPU-par-GF-table", () =>
                {
                    CpuParallelStego.EncryptToFile(input, file, st, gpuFiller: true, parallel: true);
                    var r = CpuParallelStego.DecryptFromFile(file, input.Length, st, CpuRecognition.Table, parallel: true);
                    if (!r.AsSpan().SequenceEqual(input)) throw new InvalidOperationException("verification failed");
                }),
                new("CPU-opt", () =>
                {
                    optN.EncryptToFile(input, file);
                    optN.DecryptFromFile(file, result, input.Length);
                    if (!result.AsSpan(0, input.Length).SequenceEqual(input)) throw new InvalidOperationException("verification failed");
                }),
            }, () => { NewKey(inp); Array.Clear(result); }, null);
        }
        if (File.Exists(file)) File.Delete(file);
    }
}
