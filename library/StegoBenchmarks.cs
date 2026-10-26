// BenchmarkDotNet benchmarks of configuration A: listing from Appendix 2 of the master's thesis on which the
// paper is based. Reproduced as printed there; only the indentation was restored (no using directives).

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
[Config(typeof(Config))]
public class StegoBenchmarks
{
    private string[] etalons = Array.Empty<string>();
    private byte[] fragmentBytes = Array.Empty<byte>();
    private AssocStego aStego = null!;
    private AssocStego loadedStego = null!;
    private string pathKey = string.Empty;
    private string pathStego = string.Empty;
    private string pathDecryptedFragment = string.Empty;
    private AcceleratorSession? integratedGpuSession;
    private AcceleratorSession? discreteGpuSession;
    private GpuStegoEncryptor? gpuEncryptor;
    private GpuStegoDecryptor? gpuDecryptor;
    private long inputFileSizeBytes;
    private int benchmarkDataSizeBytes;
    [GlobalSetup]
    public void GlobalSetup()
    {
        Console.WriteLine("Инициализация данных для бенчмарков...");
        etalons = GetDefaultEtalons10();
        string bookPath = BenchmarkRuntimeOptions.ResolveInputFilePathOrThrow();
        string fullText = GetFullText(bookPath);
        fragmentBytes = Encoding.UTF8.GetBytes(fullText);
        inputFileSizeBytes = new FileInfo(bookPath).Length;
        benchmarkDataSizeBytes = fragmentBytes.Length;
        string benchmarkOutputDirectory = BenchmarkRuntimeOptions.ResolveOrCreateOutputDirectory();
        pathKey = Path.Combine(benchmarkOutputDirectory, $"benchmark_{Guid.NewGuid():N}_key.bin");
        pathStego = Path.Combine(benchmarkOutputDirectory, $"benchmark_{Guid.NewGuid():N}_stego.bin");
        pathDecryptedFragment = Path.Combine(benchmarkOutputDirectory, $"benchmark_{Guid.NewGuid():N}_decrypted.txt");
        integratedGpuSession = BenchmarkHardwareInfo.IntegratedGpu.IsAvailable
        ? AcceleratorSelector.Create(ExecutionMode.IntegratedGpu)
        : null;
        discreteGpuSession = BenchmarkHardwareInfo.BestDiscreteGpu.IsAvailable
        ? AcceleratorSelector.CreateBestDiscreteGpuForBenchmarks()
        : null;
        PrintBenchmarkEnvironment();
        SaveEnvironmentInfo();
    }
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        Console.WriteLine("Очистка ресурсов...");
        DisposeGpuProcessors();
        try
        {
            if (File.Exists(pathKey)) File.Delete(pathKey);
            if (File.Exists(pathStego)) File.Delete(pathStego);
            if (File.Exists(pathDecryptedFragment)) File.Delete(pathDecryptedFragment);
        }
        catch
        {
        }
        integratedGpuSession?.Dispose();
        integratedGpuSession = null;
        discreteGpuSession?.Dispose();
        discreteGpuSession = null;
    }
    [IterationCleanup]
    public void IterationCleanup()
    {
        DisposeGpuProcessors();
    }
    private void PrintBenchmarkEnvironment()
    {
        Console.WriteLine("Доступные профили бенчмарка:");
        PrintProfile(BenchmarkHardwareInfo.Cpu);
        PrintProfile(BenchmarkHardwareInfo.IntegratedGpu);
        PrintProfile(BenchmarkHardwareInfo.BestDiscreteGpu);
    }
    private static void PrintProfile(BenchmarkHardwareDescriptor descriptor)
    {
        string status = descriptor.IsAvailable ? "доступен" : "пропущен";
        Console.WriteLine($" {descriptor.Approach}: {status}; Backend: {descriptor.Backend}; Device: {descriptor.DeviceName}");
    }
    private void SaveEnvironmentInfo()
    {
        string outputDirectory = BenchmarkRuntimeOptions.ResolveOrCreateOutputDirectory();
        string reportPath = Path.Combine(outputDirectory, "BenchmarkEnvironment.txt");
        File.WriteAllLines(reportPath, new[]
        {
            $"CPU: {BenchmarkHardwareInfo.Cpu.DeviceName}",
            $"Integrated GPU: {(BenchmarkHardwareInfo.IntegratedGpu.IsAvailable ? BenchmarkHardwareInfo.IntegratedGpu.DeviceName : "не найдено, замеры пропущены")}",
            $"Best discrete GPU: {(BenchmarkHardwareInfo.BestDiscreteGpu.IsAvailable ? BenchmarkHardwareInfo.BestDiscreteGpu.DeviceName : "не найдено, замеры пропущены")}",
            $"Файл: {FormatSize(inputFileSizeBytes)}",
            $"Данные в benchmark: {FormatSize(benchmarkDataSizeBytes)}",
            $"Эталонов: {EtalonFactory.DefaultEtalonCount}",
            $"Длина эталона: {EtalonFactory.DefaultEtalonBitLength} бит",
            $"Расширение стегопредставления: x{CalculateStegoExpansionFactor():F2}",
            "",
            "Примечание: если интегрированное видео-ядро или дискретная GPU не найдены, соответствующие benchmark-методы исключаются фильтром AvailableHardwareBenchmarkFilter."
        });
    }
    private static string FormatSize(long bytes)
    {
        double kb = bytes / 1024.0;
        double mb = kb / 1024.0;
        return $"{bytes} байт ({kb:F2} КБ, {mb:F2} МБ)";
    }
    private static double CalculateStegoExpansionFactor()
    {
        int containerByteLen = (EtalonFactory.DefaultEtalonBitLength + 7) >> 3;
        return 3.0 * containerByteLen;
    }
    private void CommonIterationSetup(Accelerator? selectedAccelerator)
    {
        aStego = new AssocStego(etalons);
        var key = aStego.CreateKey();
        aStego.SaveKeyToFile(pathKey, key);
        loadedStego = new AssocStego(etalons);
        loadedStego.LoadKeyFromFile(pathKey);
        DisposeGpuProcessors();
        if (selectedAccelerator != null)
        {
            gpuEncryptor = new GpuStegoEncryptor(selectedAccelerator, aStego);
            gpuDecryptor = new GpuStegoDecryptor(selectedAccelerator, loadedStego);
        }
    }
    private void DisposeGpuProcessors()
    {
        gpuEncryptor?.Dispose();
        gpuEncryptor = null;
        gpuDecryptor?.Dispose();
        gpuDecryptor = null;
    }
    private Accelerator? IntegratedGpuAccelerator => integratedGpuSession?.Accelerator;
    private Accelerator? DiscreteGpuAccelerator => discreteGpuSession?.Accelerator;
    private bool HasGpuProcessors() => gpuEncryptor != null && gpuDecryptor != null;
    private void EnsureGpuProcessorsAvailable(string profileName)
    {
        if (!HasGpuProcessors())
        throw new InvalidOperationException($"Профиль {profileName} недоступен. Benchmark должен быть исключен фильтром доступного оборудования.");
    }
    [IterationSetup(Target = nameof(CPU_EncryptAndDecrypt))]
    public void IterationSetupCPU_Full() => CommonIterationSetup(selectedAccelerator: null);
    [IterationSetup(Target = nameof(IntegratedGpu_EncryptAndDecrypt))]
    public void IterationSetupIntegratedGpu_Full() => CommonIterationSetup(IntegratedGpuAccelerator);
    [IterationSetup(Target = nameof(DiscreteGpu_EncryptAndDecrypt))]
    public void IterationSetupDiscreteGpu_Full() => CommonIterationSetup(DiscreteGpuAccelerator);
    [IterationSetup(Target = nameof(CPU_EncryptOnly))]
    public void IterationSetupCPU_Encrypt() => CommonIterationSetup(selectedAccelerator: null);
    [IterationSetup(Target = nameof(IntegratedGpu_EncryptOnly))]
    public void IterationSetupIntegratedGpu_Encrypt() => CommonIterationSetup(IntegratedGpuAccelerator);
    [IterationSetup(Target = nameof(DiscreteGpu_EncryptOnly))]
    public void IterationSetupDiscreteGpu_Encrypt() => CommonIterationSetup(DiscreteGpuAccelerator);
    [IterationSetup(Target = nameof(CPU_DecryptOnly))]
    public void IterationSetupCPU_Decrypt()
    {
        CommonIterationSetup(selectedAccelerator: null);
        EncryptFragmentToFile(pathStego, fragmentBytes, aStego);
    }
    [IterationSetup(Target = nameof(IntegratedGpu_DecryptOnly))]
    public void IterationSetupIntegratedGpu_Decrypt()
    {
        CommonIterationSetup(IntegratedGpuAccelerator);
        EnsureGpuProcessorsAvailable("Integrated GPU");
        gpuEncryptor!.EncryptToStream(fragmentBytes, pathStego);
    }
    [IterationSetup(Target = nameof(DiscreteGpu_DecryptOnly))]
    public void IterationSetupDiscreteGpu_Decrypt()
    {
        CommonIterationSetup(DiscreteGpuAccelerator);
        EnsureGpuProcessorsAvailable("Discrete GPU");
        gpuEncryptor!.EncryptToStream(fragmentBytes, pathStego);
    }
    [Benchmark(Description = "CPU - Шифрование и дешифрование")]
    public void CPU_EncryptAndDecrypt()
    {
        EncryptFragmentToFile(pathStego, fragmentBytes, aStego);
        byte[] decryptedBytes = DecryptFragmentBytesFromFile(pathStego, fragmentBytes.Length, loadedStego);
        if (!VerifyFragments(fragmentBytes, decryptedBytes))
        throw new InvalidOperationException("Верификация не пройдена!");
    }
    [Benchmark(Description = "Интегрированное GPU - Шифрование и дешифрование")]
    public void IntegratedGpu_EncryptAndDecrypt()
    {
        EnsureGpuProcessorsAvailable("Integrated GPU");
        gpuEncryptor!.EncryptToStream(fragmentBytes, pathStego);
        byte[] decryptedBytes = gpuDecryptor!.DecryptFromFile(pathStego, fragmentBytes.Length);
        if (!VerifyFragments(fragmentBytes, decryptedBytes))
        throw new InvalidOperationException("Верификация не пройдена!");
    }
    [Benchmark(Description = "Дискретное GPU - Шифрование и дешифрование")]
    public void DiscreteGpu_EncryptAndDecrypt()
    {
        EnsureGpuProcessorsAvailable("Discrete GPU");
        gpuEncryptor!.EncryptToStream(fragmentBytes, pathStego);
        byte[] decryptedBytes = gpuDecryptor!.DecryptFromFile(pathStego, fragmentBytes.Length);
        if (!VerifyFragments(fragmentBytes, decryptedBytes))
        throw new InvalidOperationException("Верификация не пройдена!");
    }
    [Benchmark(Description = "Только шифрование CPU")]
    public void CPU_EncryptOnly()
    {
        EncryptFragmentToFile(pathStego, fragmentBytes, aStego);
    }
    [Benchmark(Description = "Только шифрование интегрированное GPU")]
    public void IntegratedGpu_EncryptOnly()
    {
        EnsureGpuProcessorsAvailable("Integrated GPU");
        gpuEncryptor!.EncryptToStream(fragmentBytes, pathStego);
    }
    [Benchmark(Description = "Только шифрование дискретное GPU")]
    public void DiscreteGpu_EncryptOnly()
    {
        EnsureGpuProcessorsAvailable("Discrete GPU");
        gpuEncryptor!.EncryptToStream(fragmentBytes, pathStego);
    }
    [Benchmark(Description = "Только дешифрование CPU")]
    public void CPU_DecryptOnly()
    {
        DecryptFragmentBytesFromFile(pathStego, fragmentBytes.Length, loadedStego);
    }
    [Benchmark(Description = "Только дешифрование интегрированное GPU")]
    public void IntegratedGpu_DecryptOnly()
    {
        EnsureGpuProcessorsAvailable("Integrated GPU");
        gpuDecryptor!.DecryptFromFile(pathStego, fragmentBytes.Length);
    }
    [Benchmark(Description = "Только дешифрование дискретное GPU")]
    public void DiscreteGpu_DecryptOnly()
    {
        EnsureGpuProcessorsAvailable("Discrete GPU");
        gpuDecryptor!.DecryptFromFile(pathStego, fragmentBytes.Length);
    }
    private string[] GetDefaultEtalons10() => EtalonFactory.CreateDefaultEtalons10();
    private static FileStream CreateSequentialWriteStream(string path) =>
    new FileStream(path, new FileStreamOptions
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Share = FileShare.None,
        BufferSize = 1 << 20,
        Options = FileOptions.SequentialScan
    });
    private static FileStream CreateSequentialReadStream(string path) =>
    new FileStream(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        BufferSize = 1 << 20,
        Options = FileOptions.SequentialScan
    });
    private void EncryptFragmentToFile(string outputPath, byte[] data, AssocStego stego)
    {
        using FileStream fs = CreateSequentialWriteStream(outputPath);
        using AssocStegoStream s = new AssocStegoStream(fs, stego);
        s.Write(data, 0, data.Length);
    }
    private byte[] DecryptFragmentBytesFromFile(string pathStego, int expectedByteLength, AssocStego stego)
    {
        byte[] result = new byte[expectedByteLength];
        using FileStream fs = CreateSequentialReadStream(pathStego);
        using AssocStegoStream s = new AssocStegoStream(fs, stego);
        int total = 0;
        while (total < expectedByteLength)
        {
            int r = s.Read(result, total, expectedByteLength - total);
            if (r == 0)
            break;
            total += r;
        }
        if (total < expectedByteLength)
        Array.Resize(ref result, total);
        return result;
    }
    private string GetFullText(string bookPath)
    {
        try
        {
            return File.ReadAllText(bookPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ошибка при чтении файла: {ex.Message}");
        }
    }
    private static bool VerifyFragments(byte[] original, byte[] decrypted)
    {
        if (original == null || decrypted == null)
        return false;
        if (original.Length != decrypted.Length)
        return false;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != decrypted[i])
            return false;
        }
        return true;
    }
}
public class Config : ManualConfig
{
    public Config()
    {
        AddJob(Job.Default
        .WithInvocationCount(1)
        .WithUnrollFactor(1)
        .WithId("Default"));
        AddFilter(new AvailableHardwareBenchmarkFilter());
        AddLogger(ConsoleLogger.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Error);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.StdErr);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.Q1);
        AddColumn(StatisticColumn.Q3);
        AddColumn(StatisticColumn.Max);
        AddColumn(new BackendColumn());
        AddColumn(new DeviceColumn());
        AddColumn(new OperationsPerSecondColumn());
        AddColumn(new PayloadSizeColumn());
        // Важно: этот diagnoser возвращает Gen0/Gen1/Gen2 и Allocated.
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);
        AddExporter(HtmlExporter.Default);
        AddValidator(JitOptimizationsValidator.DontFailOnError);
    }
}
public class OperationsPerSecondColumn : IColumn
{
    public string Id => "OperationsPerSecond";
    public string ColumnName => "Ops/sec";
    public string Legend => "Operations per second (higher is better)";
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
    GetValue(summary, benchmarkCase);
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        var report = summary.Reports.FirstOrDefault(r => r.BenchmarkCase == benchmarkCase);
        if (report?.ResultStatistics == null) return "---";
        var meanTimeInSeconds = report.ResultStatistics.Mean / 1_000_000_000.0; // наносекунды в секунды
        var opsPerSecond = 1.0 / meanTimeInSeconds;
        return opsPerSecond.ToString("F2");
    }
    public bool IsAvailable(Summary summary) => true;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
}
internal enum BenchmarkHardwareProfile
{
    Cpu,
    IntegratedGpu,
    BestDiscreteGpu
}
internal sealed record BenchmarkHardwareDescriptor(
BenchmarkHardwareProfile Profile,
bool IsAvailable,
string Approach,
string Backend,
string DeviceName);
internal static class BenchmarkHardwareInfo
{
    private static readonly Lazy<BenchmarkHardwareDescriptor> CpuDescriptor = new(CreateCpuDescriptor);
    private static readonly Lazy<BenchmarkHardwareDescriptor> IntegratedGpuDescriptor = new(CreateIntegratedGpuDescriptor);
    private static readonly Lazy<BenchmarkHardwareDescriptor> BestDiscreteGpuDescriptor = new(CreateBestDiscreteGpuDescriptor);
    public static BenchmarkHardwareDescriptor Cpu => CpuDescriptor.Value;
    public static BenchmarkHardwareDescriptor IntegratedGpu => IntegratedGpuDescriptor.Value;
    public static BenchmarkHardwareDescriptor BestDiscreteGpu => BestDiscreteGpuDescriptor.Value;
    public static BenchmarkHardwareDescriptor GetDescriptor(BenchmarkHardwareProfile profile) => profile switch
    {
        BenchmarkHardwareProfile.Cpu => Cpu,
        BenchmarkHardwareProfile.IntegratedGpu => IntegratedGpu,
        BenchmarkHardwareProfile.BestDiscreteGpu => BestDiscreteGpu,
        _ => Cpu
    };
    public static BenchmarkHardwareProfile GetProfileFromMethodName(string methodName)
    {
        if (methodName.StartsWith("IntegratedGpu_", StringComparison.OrdinalIgnoreCase))
        return BenchmarkHardwareProfile.IntegratedGpu;
        if (methodName.StartsWith("DiscreteGpu_", StringComparison.OrdinalIgnoreCase))
        return BenchmarkHardwareProfile.BestDiscreteGpu;
        return BenchmarkHardwareProfile.Cpu;
    }
    private static BenchmarkHardwareDescriptor CreateCpuDescriptor()
    {
        return new BenchmarkHardwareDescriptor(
        BenchmarkHardwareProfile.Cpu,
        true,
        "CPU",
        "CPU",
        GetCpuName());
    }
    private static BenchmarkHardwareDescriptor CreateIntegratedGpuDescriptor()
    {
        try
        {
            using var session = AcceleratorSelector.Create(ExecutionMode.IntegratedGpu);
            if (session.IsCpuFallback || session.Accelerator == null)
            {
                return new BenchmarkHardwareDescriptor(
                BenchmarkHardwareProfile.IntegratedGpu,
                false,
                "Integrated GPU",
                "OpenCL iGPU",
                "Интегрированное видео-ядро Intel/AMD не найдено");
            }
            return new BenchmarkHardwareDescriptor(
            BenchmarkHardwareProfile.IntegratedGpu,
            true,
            "Integrated GPU",
            "OpenCL iGPU",
            session.Accelerator.Name);
        }
        catch (Exception ex)
        {
            return new BenchmarkHardwareDescriptor(
            BenchmarkHardwareProfile.IntegratedGpu,
            false,
            "Integrated GPU",
            "OpenCL iGPU",
            $"Интегрированное видео-ядро Intel/AMD недоступно: {ex.Message}");
        }
    }
    private static BenchmarkHardwareDescriptor CreateBestDiscreteGpuDescriptor()
    {
        try
        {
            using var session = AcceleratorSelector.CreateBestDiscreteGpuForBenchmarks();
            if (session.IsCpuFallback || session.Accelerator == null)
            {
                return new BenchmarkHardwareDescriptor(
                BenchmarkHardwareProfile.BestDiscreteGpu,
                false,
                "Discrete GPU",
                "CUDA/OpenCL dGPU",
                "Дискретная видеокарта NVIDIA/AMD не найдена");
            }
            string acceleratorName = session.Accelerator.Name;
            string backend = acceleratorName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            ? "CUDA dGPU"
            : "OpenCL dGPU";
            return new BenchmarkHardwareDescriptor(
            BenchmarkHardwareProfile.BestDiscreteGpu,
            true,
            "Discrete GPU",
            backend,
            acceleratorName);
        }
        catch (Exception ex)
        {
            return new BenchmarkHardwareDescriptor(
            BenchmarkHardwareProfile.BestDiscreteGpu,
            false,
            "Discrete GPU",
            "CUDA/OpenCL dGPU",
            $"Дискретная видеокарта NVIDIA/AMD недоступна: {ex.Message}");
        }
    }
    private static string GetCpuName()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT Name FROM Win32_Processor");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            }
        }
        catch
        {
        }
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0");
            var name = key?.GetValue("ProcessorNameString")?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        }
        catch
        {
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
        ?? "Неизвестный CPU";
    }
}
internal sealed class AvailableHardwareBenchmarkFilter : IFilter
{
    public bool Predicate(BenchmarkCase benchmarkCase)
    {
        var methodName = benchmarkCase.Descriptor.WorkloadMethod.Name;
        var profile = BenchmarkHardwareInfo.GetProfileFromMethodName(methodName);
        return BenchmarkHardwareInfo.GetDescriptor(profile).IsAvailable;
    }
}
public sealed class BackendColumn : IColumn
{
    public string Id => nameof(BackendColumn);
    public string ColumnName => "Backend";
    public string Legend => "Технологический backend: CPU, OpenCL iGPU, CUDA dGPU или OpenCL dGPU";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 1;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        var profile = BenchmarkHardwareInfo.GetProfileFromMethodName(benchmarkCase.Descriptor.WorkloadMethod.Name);
        return BenchmarkHardwareInfo.GetDescriptor(profile).Backend;
    }
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
    GetValue(summary, benchmarkCase);
}
public sealed class DeviceColumn : IColumn
{
    public string Id => nameof(DeviceColumn);
    public string ColumnName => "Device";
    public string Legend => "Фактически выбранное вычислительное устройство";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 2;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        var profile = BenchmarkHardwareInfo.GetProfileFromMethodName(benchmarkCase.Descriptor.WorkloadMethod.Name);
        return BenchmarkHardwareInfo.GetDescriptor(profile).DeviceName;
    }
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
    GetValue(summary, benchmarkCase);
}
public sealed class PayloadSizeColumn : IColumn
{
    public string Id => nameof(PayloadSizeColumn);
    public string ColumnName => "Input MB";
    public string Legend => "Размер исходных данных, обработанных за одну операцию benchmark";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 3;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
    (BenchmarkPayloadMetrics.GetPayloadBytes() / 1024.0 / 1024.0).ToString("F2");
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
    GetValue(summary, benchmarkCase);
}
internal static class BenchmarkPayloadMetrics
{
    public static long GetPayloadBytes()
    {
        try
        {
            string path = BenchmarkRuntimeOptions.ResolveInputFilePathOrThrow();
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
    public static double GetStegoExpansionFactor()
    {
        int containerByteLen = (EtalonFactory.DefaultEtalonBitLength + 7) >> 3;
        return 3.0 * containerByteLen;
    }
}
