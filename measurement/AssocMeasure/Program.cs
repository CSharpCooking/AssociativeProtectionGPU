using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class P
{
    static readonly string TmpDir = Path.Combine(Path.GetTempPath(), "assoc_measure");
    static readonly List<object> Results = new();

    static int Main(string[] args)
    {
        Directory.CreateDirectory(TmpDir);
        string cmd = args.Length > 0 ? args[0] : "help";
        switch (cmd)
        {
            case "keys": Keys(int.Parse(args[1])); break;
            case "equiv": Equiv(int.Parse(args[1]), int.Parse(args[2])); break;
            case "filler": Filler(args[1], int.Parse(args[2]), args.Length > 3 ? args[3] : null); break;
            case "form": Form(args[1], int.Parse(args[2]), int.Parse(args[3]), args[4]); break;
            case "rec": Rec(args[1], int.Parse(args[2]), int.Parse(args[3]), args[4]); break;
            case "env": Env(); break;
            case "dist": Dist(int.Parse(args[1]), int.Parse(args[2]), args[3] == "1", args[4], args[5..]); break;
            default: Console.WriteLine("keys N | equiv K M | filler outdir bytes [seed1,seed2,seed3] | form input iters warmup out.json | rec input iters warmup out.json | dist keys reps withBranchFree(0/1) out.json input1 [input2 ...] | env"); return 1;
        }
        return 0;
    }

    static void Env()
    {
        Console.WriteLine($".NET {Environment.Version}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
                          $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}; " +
                          $"ServerGC={System.Runtime.GCSettings.IsServerGC}; Stopwatch.Frequency={Stopwatch.Frequency}");
    }

    // ------------------------------------------------------------------ key statistics and Proposition 1
    static void Keys(int n)
    {
        string[] et = EtalonFactory.CreateDefaultEtalons10();
        var weightHist = new long[11]; var pHist = new long[17];
        double sumMean = 0; long cond5Fail = 0, prop1Fail = 0, dupPos = 0;
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < n; k++)
        {
            var st = new AssocStego(et);
            var key = st.CreateKey();
            st.ValidateKeysOrThrow(key);                       // the check performed by the library
            double s = 0;
            for (int t = 0; t < 10; t++)
            {
                var lst = st.MaskUnitPositions[t];
                if (lst.Distinct().Count() != lst.Count) dupPos++;
                int w = lst.Count; weightHist[Math.Min(w, 10)]++; s += w;
            }
            sumMean += s / 10;
            // condition (5): for every pair, a common mask position where the etalons differ
            for (int d = 0; d < 10; d++)
                for (int t = d + 1; t < 10; t++)
                {
                    bool ok = st.MaskUnitPositions[d].Intersect(st.MaskUnitPositions[t])
                                .Any(p => st.etalons[d].GetBit(p) != st.etalons[t].GetBit(p));
                    if (!ok) cond5Fail++;
                }
            // Proposition 1 (second statement): every pattern on P matches exactly one etalon
            try
            {
                var tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10);
                pHist[tb.M]++;
            }
            catch (InvalidOperationException) { prop1Fail++; }
        }
        double total = weightHist.Sum();
        Console.WriteLine($"keys={n}  time={sw.Elapsed.TotalSeconds:F1}s  condition(5) violations={cond5Fail}  Proposition-1 violations={prop1Fail}  repeated positions on a path={dupPos}");
        Console.WriteLine($"mean mask weight (mean over keys) = {sumMean / n:F4}");
        Console.WriteLine("mask weight distribution: " + string.Join(", ", Enumerable.Range(0, 11).Where(w => weightHist[w] > 0).Select(w => $"{w}:{weightHist[w] / total:P2}")));
        Console.WriteLine("|P| distribution: " + string.Join(", ", Enumerable.Range(0, 17).Where(m => pHist[m] > 0).Select(m => $"{m}:{pHist[m] / (double)n:P2}")));
        File.WriteAllText(Path.Combine(TmpDir, "keys.json"), JsonSerializer.Serialize(new { n, meanWeight = sumMean / n, weightHist, pHist, cond5Fail, prop1Fail, dupPos }));
    }

    // ------------------------------------------------------------------ data and key dependence of recognition (paired design)
    // The same K keys are used for every input. For each key and input: the protected representation is formed
    // (chunk-wise generator), and the original search, the table-driven and the branch-free recognition are timed;
    // search and table: minimum of `reps` runs, branch-free: one run. Every run is verified byte by byte.
    // Key descriptors: |P| and the mean number of section bytes the byte-wise comparison inspects per tried etalon
    // (counted on the first 64 KB of the first input).
    static void Dist(int keys, int reps, bool withBranchFree, string outJson, string[] inputs)
    {
        var data = inputs.Select(File.ReadAllBytes).ToArray();
        int len = data[0].Length; if (data.Any(d => d.Length != len)) throw new ArgumentException("inputs must have equal length");
        double mb = len / 1048576.0;
        string[] et = EtalonFactory.CreateDefaultEtalons10();
        var hidden = new byte[len * 30]; var output = new byte[len];
        var rows = new List<object>();
        double Time(Action a) { GC.Collect(); GC.WaitForPendingFinalizers(); var sw = Stopwatch.StartNew(); a(); sw.Stop(); return sw.Elapsed.TotalMilliseconds; }
        void Check(byte[] input) { if (!output.AsSpan().SequenceEqual(input)) throw new InvalidOperationException("verification failed"); }
        for (int k = 0; k < keys; k++)
        {
            var st = new AssocStego(et); st.CreateKey();
            var tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10);
            int c = st.ContainerByteLength; byte[] kb = st.FlattenedKeysBytes, me = st.FlattenedMaskedEtalonsBytes;
            // key descriptor: bytes inspected per tried etalon on the first 64 KB of the first input
            double bytesPerTried;
            {
                byte[] slice = data[0].AsSpan(0, Math.Min(len, 65536)).ToArray();
                byte[] hs = new byte[slice.Length * 30];
                CpuVariants.FormBulkCsprng(st, slice, hs);
                long tried = 0, inspected = 0;
                for (int i = 0; i < slice.Length; i++)
                    for (int sct = 0; sct < 3; sct++)
                    {
                        int off = i * 30 + sct * c;
                        for (int t = 0; t < 10; t++)
                        {
                            tried++; bool ok = true;
                            for (int j = 0; j < c; j++) { inspected++; if ((hs[off + j] & kb[t * c + j]) != me[t * c + j]) { ok = false; break; } }
                            if (ok) break;
                        }
                    }
                bytesPerTried = (double)inspected / tried;
            }
            for (int q = 0; q < data.Length; q++)
            {
                int ii = (q + k) % data.Length;            // rotate the order of the inputs between keys
                byte[] input = data[ii];
                CpuVariants.FormBulkCsprng(st, input, hidden);
                double search = double.MaxValue, table = double.MaxValue;
                for (int r = 0; r < reps; r++)
                {
                    search = Math.Min(search, Time(() => CpuVariants.RecOriginalNoStream(st, hidden, output))); Check(input); Array.Clear(output);
                    table = Math.Min(table, Time(() => CpuVariants.RecTable(tb, st, hidden, output))); Check(input); Array.Clear(output);
                }
                double bf = double.NaN; if (withBranchFree) { bf = Time(() => CpuVariants.RecBranchFree(st, hidden, output)); Check(input); Array.Clear(output); }
                rows.Add(new { key = k, input = Path.GetFileName(inputs[ii]), m = tb.M, bytesPerTriedEtalon = bytesPerTried,
                               searchMsPerMB = search / mb, tableMsPerMB = table / mb, branchFreeMsPerMB = withBranchFree ? bf / mb : -1.0 });
                Console.WriteLine($"key {k,2} {Path.GetFileName(inputs[ii]),-18} m={tb.M} search={search / mb,7:F1} table={table / mb,6:F1} branch-free={bf / mb,6:F1} ms/MB");
            }
            File.WriteAllText(outJson, JsonSerializer.Serialize(rows));   // partial results after every key
        }
    }

    // ------------------------------------------------------------------ equivalence of the decoders
    static void Equiv(int keys, int m)
    {
        string[] et = EtalonFactory.CreateDefaultEtalons10();
        long bad = 0, total = 0;
        var rng = new Random(12345);
        for (int k = 0; k < keys; k++)
        {
            var st = new AssocStego(et); st.CreateKey();
            var tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10);
            var input = new byte[m]; rng.NextBytes(input);
            var hidden = new byte[m * 30];
            if (k % 2 == 0) CpuVariants.FormGpuFunction(st, input, hidden); else CpuVariants.FormOriginalNoStream(st, input, hidden);
            var a = new byte[m]; var b = new byte[m]; var c = new byte[m]; var d = new byte[m];
            CpuVariants.RecOriginalNoStream(st, hidden, a);
            CpuVariants.RecGpuSearchLogic(st, hidden, b);
            CpuVariants.RecBranchFree(st, hidden, c);
            CpuVariants.RecTable(tb, st, hidden, d);
            for (int i = 0; i < m; i++) { total++; if (a[i] != input[i] || b[i] != input[i] || c[i] != input[i] || d[i] != input[i]) bad++; }
            // also random 80-bit vectors (not produced by formation): all decoders must agree
            var rv = new byte[30 * 1000]; rng.NextBytes(rv);
            for (int i = 0; i < rv.Length; i += 10) { rv[i] &= 0x3F; }   // bits 78-79 (byte 0, bits 6-7) are padding
            var o1 = new byte[1000]; var o2 = new byte[1000]; var o3 = new byte[1000];
            CpuVariants.RecGpuSearchLogic(st, rv, o1); CpuVariants.RecBranchFree(st, rv, o2); CpuVariants.RecTable(tb, st, rv, o3);
            for (int i = 0; i < 1000; i++) if (o1[i] != o2[i] || o1[i] != o3[i]) bad++;
        }
        Console.WriteLine($"equivalence: keys={keys} bytes/key={m} checked={total} mismatches={bad}");
    }

    // ------------------------------------------------------------------ filler streams for NIST SP 800-22
    // The seeds are drawn from the system generator unless given (comma-separated) to regenerate published sequences.
    static void Filler(string outDir, int bytes, string? seedList)
    {
        Directory.CreateDirectory(outDir);
        var seeds = seedList != null
            ? seedList.Split(',').Select(x => int.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray()
            : new[] { RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue), RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue), RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue) };
        for (int s = 0; s < seeds.Length; s++)
        {
            var buf = new byte[bytes]; int pos = 0;
            for (int g = 0; pos < bytes; g++)
                for (int d = 0; d < 3 && pos < bytes; d++)
                    for (int b = 0; b < 10 && pos < bytes; b++)
                        buf[pos++] = PseudoRandomByteCopy(g, d, b, seeds[s]);
            File.WriteAllBytes(Path.Combine(outDir, $"gpu_filler_{s}.bin"), buf);
        }
        var c = new byte[bytes]; RandomNumberGenerator.Fill(c); File.WriteAllBytes(Path.Combine(outDir, "csprng.bin"), c);
        Console.WriteLine($"filler files written; seeds={string.Join(",", seeds)}");
    }
    // identical to GpuStegoEncryptor.PseudoRandomByte (called through the kernel class to avoid divergence)
    static byte PseudoRandomByteCopy(int g, int d, int b, int seed) => ThesisKernels.PseudoRandomByte(g, d, b, seed);

    // ------------------------------------------------------------------ formation (forward stage)
    static FileStream SeqWrite(string path) => new(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
    static FileStream SeqRead(string path) => new(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });

    static void Form(string inputPath, int iters, int warmup, string outJson)
    {
        byte[] input = File.ReadAllBytes(inputPath);
        double mb = input.Length / 1048576.0;
        string[] et = EtalonFactory.CreateDefaultEtalons10();
        AssocStego st = new(et); st.CreateKey();
        byte[] hidden = new byte[input.Length * 30];
        string file = Path.Combine(TmpDir, "stego.bin");
        Action newKey = () => { st = new AssocStego(et); st.CreateKey(); };
        var res = new List<Stat>
        {
            Harness.Measure("form: original stream -> file", () => { using var fs = SeqWrite(file); using var s = new AssocStegoStream(fs, st); s.Write(input, 0, input.Length); }, warmup, iters, newKey),
            Harness.Measure("form: original stream -> Stream.Null", () => { using var s = new AssocStegoStream(Stream.Null, st); s.Write(input, 0, input.Length); }, warmup, iters, newKey),
            Harness.Measure("form: original, no stream (per-container CSPRNG)", () => CpuVariants.FormOriginalNoStream(st, input, hidden), warmup, iters, newKey),
            Harness.Measure("form: bulk CSPRNG (one call per 512 KB)", () => CpuVariants.FormBulkCsprng(st, input, hidden), warmup, iters, newKey),
            Harness.Measure("form: GPU filler function on CPU", () => CpuVariants.FormGpuFunction(st, input, hidden), warmup, iters, newKey),
            Harness.Measure("form: masking only (no filler)", () => CpuVariants.FormZeroFiller(st, input, hidden), warmup, iters, newKey),
            Harness.Measure($"form: bulk CSPRNG, {Environment.ProcessorCount} threads", () => CpuVariants.Par(input.Length, 64 * 1024, (a, b) => CpuVariants.FormBulkCsprng(st, input, hidden, a, b)), warmup, iters, newKey),
            Harness.Measure($"form: GPU filler function, {Environment.ProcessorCount} threads", () => CpuVariants.Par(input.Length, 64 * 1024, (a, b) => CpuVariants.FormGpuFunction(st, input, hidden, a, b)), warmup, iters, newKey),
        };
        foreach (var r in res) Console.WriteLine(Harness.Fmt(r, mb));
        File.WriteAllText(outJson, JsonSerializer.Serialize(new { input = inputPath, bytes = input.Length, results = res }));
        File.Delete(file);
    }

    // ------------------------------------------------------------------ recognition (inverse stage)
    static void Rec(string inputPath, int iters, int warmup, string outJson)
    {
        byte[] input = File.ReadAllBytes(inputPath);
        double mb = input.Length / 1048576.0;
        string[] et = EtalonFactory.CreateDefaultEtalons10();
        AssocStego st = new(et); st.CreateKey();
        RecognitionTable tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10);
        byte[] hidden = new byte[input.Length * 30];
        byte[] output = new byte[input.Length];
        string file = Path.Combine(TmpDir, "stego_rec.bin");
        bool toFile = false;
        void Setup()
        {
            st = new AssocStego(et); st.CreateKey();
            tb = RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10);
            CpuVariants.FormBulkCsprng(st, input, hidden);
            if (toFile) File.WriteAllBytes(file, hidden);
            Array.Clear(output);
        }
        void Check() { if (!output.AsSpan().SequenceEqual(input)) throw new InvalidOperationException("verification failed"); }
        var res = new List<Stat>();
        toFile = true;
        res.Add(Harness.Measure("rec: original stream <- file", () =>
        {
            using var fs = SeqRead(file); using var s = new AssocStegoStream(fs, st);
            int total = 0; while (total < output.Length) { int r = s.Read(output, total, output.Length - total); if (r == 0) break; total += r; }
        }, warmup, iters, Setup, Check));
        toFile = false;
        res.Add(Harness.Measure("rec: original stream <- memory", () =>
        {
            using var ms = new MemoryStream(hidden, false); using var s = new AssocStegoStream(ms, st);
            int total = 0; while (total < output.Length) { int r = s.Read(output, total, output.Length - total); if (r == 0) break; total += r; }
        }, warmup, iters, Setup, Check));
        res.Add(Harness.Measure("rec: original, no stream (search, CPU code)", () => CpuVariants.RecOriginalNoStream(st, hidden, output), warmup, iters, Setup, Check));
        res.Add(Harness.Measure("rec: GPU search kernel logic on CPU", () => CpuVariants.RecGpuSearchLogic(st, hidden, output), warmup, iters, Setup, Check));
        res.Add(Harness.Measure("rec: branch-free kernel logic on CPU", () => CpuVariants.RecBranchFree(st, hidden, output), warmup, iters, Setup, Check));
        res.Add(Harness.Measure("rec: table-driven kernel logic on CPU", () => CpuVariants.RecTable(tb, st, hidden, output), warmup, iters, Setup, Check));
        res.Add(Harness.Measure($"rec: original search, {Environment.ProcessorCount} threads", () => CpuVariants.Par(output.Length, 64 * 1024, (a, b) => CpuVariants.RecOriginalNoStream(st, hidden, output, a, b)), warmup, iters, Setup, Check));
        res.Add(Harness.Measure($"rec: table-driven, {Environment.ProcessorCount} threads", () => CpuVariants.Par(output.Length, 64 * 1024, (a, b) => CpuVariants.RecTable(tb, st, hidden, output, a, b)), warmup, iters, Setup, Check));
        // cost of building the table once per key
        var tbBuild = Harness.Measure("table construction (once per key)", () => RecognitionTable.Build(st.FlattenedKeysBytes, st.FlattenedMaskedEtalonsBytes, st.ContainerByteLength, 10), 3, 30);
        foreach (var r in res) Console.WriteLine(Harness.Fmt(r, mb));
        Console.WriteLine($"table construction: mean {tbBuild.Mean * 1e6:F1} us (±{tbBuild.Err999 * 1e6:F1})");
        File.WriteAllText(outJson, JsonSerializer.Serialize(new { input = inputPath, bytes = input.Length, results = res, tableBuild = tbBuild }));
        if (File.Exists(file)) File.Delete(file);
    }
}
